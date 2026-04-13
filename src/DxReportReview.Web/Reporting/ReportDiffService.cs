using System.Drawing;
using System.Runtime.InteropServices;
using DevExpress.Drawing;
using DevExpress.XtraPrinting;
using DevExpress.XtraReports.UI;

namespace DxReportReview.Web.Reporting;

public enum DiffKind { Added, Removed, Changed }

// Diff on deserialized XtraReport graphs: pair controls, then compare layout. Not a raw XML diff.
public static class ReportDiffService
{
    // On Linux, reading Font/ForeColor can pull in GDI+ via DXFont and blow up in Docker.
    private static readonly bool UseGdiFontAndColorCompare = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    private static readonly Color ColorAdded   = Color.FromArgb(22, 163, 74);
    private static readonly Color ColorRemoved = Color.FromArgb(239, 68, 68);
    private static readonly Color ColorChanged = Color.FromArgb(59, 130, 246);

    // Same epsilon for "near enough to pair" and "same position/size" after pairing (keep in sync).
    private const float LayoutFloatTolerance = 1.25f;
    private const float SemanticMinSimilarity = 0.54f;

    public static Dictionary<string, DiffKind> ComputeDiff(XtraReport baseline, XtraReport proposed)
    {
        var baseControls = FlattenLeafControls(baseline);
        var propControls = FlattenLeafControls(proposed);

        var propToBase = new Dictionary<string, ControlEntry>(StringComparer.Ordinal);
        var matchedBaseIds = new HashSet<string>(StringComparer.Ordinal);

        void Pair(ControlEntry prop, ControlEntry bas)
        {
            propToBase[prop.Id] = bas;
            matchedBaseIds.Add(bas.Id);
        }

        // Unique Name on both sides only (duplicate names skip this pass).
        var ambiguousPropNames = NamesAppearingMoreThanOnce(propControls.Select(p => p.Control.Name));
        var ambiguousBaseNames = NamesAppearingMoreThanOnce(baseControls.Select(b => b.Control.Name));

        foreach (var prop in propControls)
        {
            if (string.IsNullOrEmpty(prop.Control.Name))
                continue;
            var name = prop.Control.Name;
            if (ambiguousPropNames.Contains(name) || ambiguousBaseNames.Contains(name))
                continue;
            var candidates = baseControls
                .Where(b => !matchedBaseIds.Contains(b.Id) && string.Equals(b.Control.Name, name, StringComparison.Ordinal))
                .ToList();
            if (candidates.Count == 1)
                Pair(prop, candidates[0]);
        }

        // Same structural path string
        foreach (var prop in propControls)
        {
            if (propToBase.ContainsKey(prop.Id))
                continue;
            foreach (var bas in baseControls)
            {
                if (matchedBaseIds.Contains(bas.Id))
                    continue;
                if (string.Equals(prop.Path, bas.Path, StringComparison.Ordinal))
                {
                    Pair(prop, bas);
                    break;
                }
            }
        }

        // Same control type, max-weight matching (Hungarian)
        foreach (var type in propControls
                     .Where(p => !propToBase.ContainsKey(p.Id))
                     .Select(p => p.Control.GetType())
                     .Distinct())
        {
            var pList = propControls.Where(p => !propToBase.ContainsKey(p.Id) && p.Control.GetType() == type).ToList();
            var bList = baseControls.Where(b => !matchedBaseIds.Contains(b.Id) && b.Control.GetType() == type).ToList();
            foreach (var (p, b) in OptimalSemanticPairs(pList, bList, SemanticMinSimilarity))
                Pair(p, b);
        }

        // Last resort: same band type + same control type + nearby LocationF
        foreach (var prop in propControls)
        {
            if (propToBase.ContainsKey(prop.Id))
                continue;
            var bas = baseControls.FirstOrDefault(b =>
                !matchedBaseIds.Contains(b.Id) &&
                string.Equals(b.BandType, prop.BandType, StringComparison.Ordinal) &&
                b.Control.GetType() == prop.Control.GetType() &&
                NearLocation(b.Control.LocationF, prop.Control.LocationF));
            if (bas is not null)
                Pair(prop, bas);
        }

        var diff = new Dictionary<string, DiffKind>(StringComparer.Ordinal);

        foreach (var prop in propControls)
        {
            if (propToBase.TryGetValue(prop.Id, out var bas))
            {
                if (HasVisualChange(bas, prop))
                    diff[prop.Id] = DiffKind.Changed;
            }
            else
            {
                diff[prop.Id] = DiffKind.Added;
            }
        }

        foreach (var bas in baseControls)
        {
            if (!matchedBaseIds.Contains(bas.Id))
                diff[bas.Id] = DiffKind.Removed;
        }

        // Same Name ended up Added+Removed → treat as move / rename in one place.
        PromoteNameMoveAddRemoveToChanged(diff, baseControls, propControls);

        return diff;
    }

    private static void PromoteNameMoveAddRemoveToChanged(
        Dictionary<string, DiffKind> diff,
        List<ControlEntry> baseControls,
        List<ControlEntry> propControls)
    {
        var addedByName = new Dictionary<string, ControlEntry>(StringComparer.Ordinal);
        foreach (var p in propControls)
        {
            if (string.IsNullOrEmpty(p.Control.Name)) continue;
            if (!diff.TryGetValue(p.Id, out var k) || k != DiffKind.Added) continue;
            if (propControls.Count(x => string.Equals(x.Control.Name, p.Control.Name, StringComparison.Ordinal) && diff.TryGetValue(x.Id, out var dk) && dk == DiffKind.Added) != 1)
                continue;
            if (baseControls.Count(x => string.Equals(x.Control.Name, p.Control.Name, StringComparison.Ordinal) && diff.TryGetValue(x.Id, out var bk) && bk == DiffKind.Removed) != 1)
                continue;
            addedByName[p.Control.Name!] = p;
        }

        foreach (var kv in addedByName)
        {
            var bas = baseControls.FirstOrDefault(b =>
                string.Equals(b.Control.Name, kv.Key, StringComparison.Ordinal) &&
                diff.TryGetValue(b.Id, out var bk) && bk == DiffKind.Removed);
            if (bas is null) continue;
            diff[kv.Value.Id] = DiffKind.Changed;
            diff.Remove(bas.Id);
        }
    }

    public static IReadOnlyList<string> GetLayoutQualityHints(XtraReport report)
    {
        var hints = new List<string>();
        var leaves = FlattenLeafControls(report);
        var withName = leaves.Where(e => !string.IsNullOrEmpty(e.Control.Name)).ToList();
        var dupNames = withName
            .GroupBy(e => e.Control.Name!, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
        foreach (var n in dupNames)
            hints.Add($"Duplicate Name \"{n}\" ({withName.Count(e => e.Control.Name == n)} times); name-based pairing skipped for that name.");

        var unnamed = leaves.Count(e => string.IsNullOrEmpty(e.Control.Name));
        if (unnamed > 0)
            hints.Add($"{unnamed} leaf control(s) have no Name; path/similarity matching only. Naming them helps reviews.");

        var bandNull = leaves.Count(e => e.Control.Band is null);
        if (bandNull > 0)
            hints.Add($"{bandNull} leaf control(s) have Band == null (unusual).");

        return hints;
    }

    private static HashSet<string> NamesAppearingMoreThanOnce(IEnumerable<string?> names)
    {
        return names
            .Where(n => !string.IsNullOrEmpty(n))
            .GroupBy(n => n!, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static List<(ControlEntry Prop, ControlEntry Base)> OptimalSemanticPairs(
        List<ControlEntry> props,
        List<ControlEntry> bases,
        float minSimilarity)
    {
        var result = new List<(ControlEntry, ControlEntry)>();
        if (props.Count == 0 || bases.Count == 0)
            return result;

        const int scale = 1000;
        var simByPair = new Dictionary<(int Pi, int Bj), float>();

        if (props.Count >= bases.Count)
        {
            var adj = new List<List<(int vertex, int weight)>>(props.Count);
            for (var i = 0; i < props.Count; i++)
            {
                var edges = new List<(int, int)>();
                for (var j = 0; j < bases.Count; j++)
                {
                    var sim = ControlSimilarity(props[i].Control, bases[j].Control);
                    if (sim >= minSimilarity)
                    {
                        simByPair[(i, j)] = sim;
                        edges.Add((j, (int)(sim * scale)));
                    }
                }
                adj.Add(edges);
            }

            var m = FastHungarian.FastHungarian.MaximumWeightMatching(props.Count, bases.Count, adj);
            for (var i = 0; i < props.Count; i++)
            {
                var j = m.LeftPairs[i];
                if (j < 0) continue;
                if (simByPair.TryGetValue((i, j), out var sim) && sim >= minSimilarity)
                    result.Add((props[i], bases[j]));
            }
        }
        else
        {
            // More bases than props: swap sides so the Hungarian impl doesn't drop edges.
            var adj = new List<List<(int vertex, int weight)>>(bases.Count);
            for (var j = 0; j < bases.Count; j++)
            {
                var edges = new List<(int, int)>();
                for (var i = 0; i < props.Count; i++)
                {
                    var sim = ControlSimilarity(props[i].Control, bases[j].Control);
                    if (sim >= minSimilarity)
                    {
                        simByPair[(i, j)] = sim;
                        edges.Add((i, (int)(sim * scale)));
                    }
                }
                adj.Add(edges);
            }

            var m = FastHungarian.FastHungarian.MaximumWeightMatching(bases.Count, props.Count, adj);
            for (var j = 0; j < bases.Count; j++)
            {
                var i = m.LeftPairs[j];
                if (i < 0) continue;
                if (simByPair.TryGetValue((i, j), out var sim) && sim >= minSimilarity)
                    result.Add((props[i], bases[j]));
            }
        }

        return result;
    }

    private static float ControlSimilarity(XRControl a, XRControl b)
    {
        if (a.GetType() != b.GetType())
            return 0f;

        float score = 0f;

        score += 0.26f * SizeSimilarity(a.SizeF, b.SizeF);
        if (UseGdiFontAndColorCompare)
        {
            score += 0.2f * FontSimilarityDx(a.Font as DXFont, b.Font as DXFont);
            score += 0.11f * (SafeColorArgbEquals(a.ForeColor, b.ForeColor) ? 1f : 0f);
            score += 0.11f * (SafeColorArgbEquals(a.BackColor, b.BackColor) ? 1f : 0f);
        }
        else
        {
            // Skia: don't touch Font/Color getters; use a small fixed bump instead of the old 0.5 neutrals.
            score += 0.2f * 0.18f;
            score += 0.11f * 0.18f;
            score += 0.11f * 0.18f;
        }
        score += 0.09f * (a.TextAlignment == b.TextAlignment ? 1f : 0f);
        score += 0.07f * PaddingSimilarity(a.Padding, b.Padding);
        score += 0.04f * (a.Visible == b.Visible ? 1f : 0f);
        score += 0.12f * (string.Equals(GetAncestorsKey(a), GetAncestorsKey(b), StringComparison.Ordinal) ? 1f : 0f);

        if (a is XRLabel la && b is XRLabel lb)
        {
            score += 0.18f * TextWeakSimilarity(la.Text, lb.Text);
            score += 0.03f * (la.WordWrap == lb.WordWrap ? 1f : 0f);
            score += 0.03f * (SafeLabelAngle(la) == SafeLabelAngle(lb) ? 1f : 0f);
        }
        else
            score += 0.08f;

        score = Math.Clamp(score, 0f, 1f);

        if (a is XRLabel la2 && b is XRLabel lb2)
        {
            var ta = (la2.Text ?? "").Trim();
            var tb = (lb2.Text ?? "").Trim();
            if (ta.Length > 0 && tb.Length > 0 && !string.Equals(ta, tb, StringComparison.OrdinalIgnoreCase))
                score *= 0.52f;
        }

        return Math.Clamp(score, 0f, 1f);
    }

    private static float TextWeakSimilarity(string? x, string? y)
    {
        if (string.IsNullOrEmpty(x) && string.IsNullOrEmpty(y))
            return 1f;
        if (string.IsNullOrEmpty(x) || string.IsNullOrEmpty(y))
            return 0.35f;
        if (string.Equals(x, y, StringComparison.Ordinal))
            return 1f;
        var ratio = Math.Min(x.Length, y.Length) / (float)Math.Max(x.Length, y.Length);
        return 0.35f + 0.25f * ratio;
    }

    private static float SizeSimilarity(SizeF a, SizeF b)
    {
        var dw = Math.Abs(a.Width - b.Width);
        var dh = Math.Abs(a.Height - b.Height);
        var scale = Math.Max(20f, Math.Max(Math.Max(a.Width, a.Height), Math.Max(b.Width, b.Height)));
        var dist = (dw + dh) / scale;
        return Math.Clamp(1f - dist, 0f, 1f);
    }

    private static float FontSimilarityDx(DXFont? fa, DXFont? fb)
    {
        if (fa is null && fb is null)
            return 1f;
        if (fa is null || fb is null)
            return 0.4f;
        try
        {
            var nameMatch = string.Equals(fa.Name, fb.Name, StringComparison.OrdinalIgnoreCase) ? 1f : 0f;
            var sizeMatch = Math.Abs(fa.Size - fb.Size) < 0.6f ? 1f : Math.Clamp(1f - Math.Abs(fa.Size - fb.Size) / 12f, 0f, 1f);
            var styleMatch = fa.Bold == fb.Bold && fa.Italic == fb.Italic ? 1f : 0.6f;
            return (nameMatch * 0.45f + sizeMatch * 0.35f + styleMatch * 0.2f);
        }
        catch
        {
            return 0.5f;
        }
    }

    private static bool SafeColorArgbEquals(Color a, Color b)
    {
        try
        {
            return a.R == b.R && a.G == b.G && a.B == b.B && a.A == b.A;
        }
        catch
        {
            return true;
        }
    }

    private static float PaddingSimilarity(PaddingInfo a, PaddingInfo b)
    {
        if (a.Left == b.Left && a.Right == b.Right && a.Top == b.Top && a.Bottom == b.Bottom)
            return 1f;
        var d = Math.Abs(a.Left - b.Left) + Math.Abs(a.Right - b.Right) + Math.Abs(a.Top - b.Top) + Math.Abs(a.Bottom - b.Bottom);
        return Math.Clamp(1f - d / 40f, 0f, 1f);
    }

    private static bool NearLocation(PointF a, PointF b) =>
        Math.Abs(a.X - b.X) < LayoutFloatTolerance && Math.Abs(a.Y - b.Y) < LayoutFloatTolerance;

    private static string GetAncestorsKey(XRControl c)
    {
        var parts = new List<string>();
        for (XRControl? x = c.Parent as XRControl; x != null; x = x.Parent as XRControl)
            parts.Add(x.GetType().Name);
        parts.Reverse();
        return string.Join("/", parts);
    }

    public static void ApplyHighlights(XtraReport report, Dictionary<string, DiffKind> diff, bool isBase)
    {
        if (diff.Count == 0) return;

        foreach (var leaf in FlattenLeafControls(report))
        {
            if (diff.TryGetValue(leaf.Id, out var kind))
                ApplyMark(leaf.Control, kind, isBase);
        }
    }

    public static byte[] SerializeReport(XtraReport report)
    {
        using var ms = new MemoryStream();
        report.SaveLayoutToXml(ms);
        return ms.ToArray();
    }

    private static readonly Lazy<byte[]> EmptyLayoutTemplateLazy = new(() => SerializeReport(new XtraReport()));

    public static byte[] GetEmptyLayoutTemplateBytes() => EmptyLayoutTemplateLazy.Value;

    public static XtraReport DeserializeReport(byte[] raw)
    {
        return Task.Run(() =>
        {
            using var ms = new MemoryStream(raw);
            return XtraReport.FromStream(ms);
        }).GetAwaiter().GetResult();
    }

    private static List<ControlEntry> FlattenLeafControls(XtraReport report)
    {
        var list = new List<ControlEntry>();
        var bandTypeCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        for (int bi = 0; bi < report.Bands.Count; bi++)
        {
            var band = report.Bands[bi];
            var bandTypeName = band.GetType().Name;
            bandTypeCounts.TryGetValue(bandTypeName, out int bti);
            bandTypeCounts[bandTypeName] = bti + 1;
            var bandPath = $"{bandTypeName}[{bti}]";

            CollectLeaves(list, band, bandPath, bandTypeName);

            var subTypeCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int si = 0; si < band.SubBands.Count; si++)
            {
                var sub = band.SubBands[si];
                var subTypeName = sub.GetType().Name;
                subTypeCounts.TryGetValue(subTypeName, out int sti);
                subTypeCounts[subTypeName] = sti + 1;
                var subPath = $"{bandPath}/{subTypeName}[{sti}]";
                CollectLeaves(list, sub, subPath, subTypeName);
            }
        }

        return list;
    }

    private static void CollectLeaves(
        List<ControlEntry> list, XRControl parent, string parentPath, string bandType)
    {
        var childTypeCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        for (int i = 0; i < parent.Controls.Count; i++)
        {
            var child = parent.Controls[i];
            var childTypeName = child.GetType().Name;
            childTypeCounts.TryGetValue(childTypeName, out int cti);
            childTypeCounts[childTypeName] = cti + 1;
            var childPath = $"{parentPath}/{childTypeName}[{cti}]";

            var id = !string.IsNullOrEmpty(child.Name) ? child.Name : childPath;
            var owningBandType = child.Band?.GetType().Name ?? bandType;
            list.Add(new ControlEntry(id, childPath, owningBandType, child));

            CollectLeaves(list, child, childPath, bandType);
        }
    }

    private static bool HasVisualChange(ControlEntry baseline, ControlEntry proposed)
    {
        return HasControlLayoutChange(baseline.Control, proposed.Control);
    }

    private static bool HasControlLayoutChange(XRControl a, XRControl b)
    {
        if (a.GetType() != b.GetType())
            return true;

        if (a.Visible != b.Visible)
            return true;

        if (a is XRLabel la && b is XRLabel lb)
        {
            if (!string.Equals(la.Text, lb.Text, StringComparison.Ordinal))
                return true;
            if (la.WordWrap != lb.WordWrap)
                return true;
            if (AngleMismatch(SafeLabelAngle(la), SafeLabelAngle(lb)))
                return true;
        }

        if (LayoutFloatMismatch(a.LocationF.X, b.LocationF.X) || LayoutFloatMismatch(a.LocationF.Y, b.LocationF.Y))
            return true;

        if (LayoutFloatMismatch(a.SizeF.Width, b.SizeF.Width) || LayoutFloatMismatch(a.SizeF.Height, b.SizeF.Height))
            return true;

        if (UseGdiFontAndColorCompare)
        {
            if (!SafeColorArgbEquals(a.ForeColor, b.ForeColor))
                return true;

            if (!SafeColorArgbEquals(a.BackColor, b.BackColor))
                return true;
        }

        if (a.TextAlignment != b.TextAlignment)
            return true;

        return false;
    }

    private static bool LayoutFloatMismatch(float a, float b) =>
        Math.Abs(a - b) > LayoutFloatTolerance;

    private static bool AngleMismatch(float a, float b) =>
        Math.Abs(a - b) > 1f;

    private static float SafeLabelAngle(XRLabel label)
    {
        try
        {
            return label.Angle;
        }
        catch
        {
            return 0f;
        }
    }

    private static void ApplyMark(XRControl ctrl, DiffKind kind, bool isBase)
    {
        var color = (kind, isBase) switch
        {
            (DiffKind.Removed, true)  => ColorRemoved,
            (DiffKind.Added, false)   => ColorAdded,
            (DiffKind.Changed, false) => ColorChanged,
            _ => (Color?)null
        };

        if (color is null) return;

        var c = color.Value;
        try
        {
            ctrl.Borders = BorderSide.All;
            ctrl.BorderColor = c;
            ctrl.BorderWidth = 3f;
            ctrl.BorderDashStyle = BorderDashStyle.Solid;
            ctrl.StylePriority.UseBorders = true;
            ctrl.StylePriority.UseBorderColor = true;
            ctrl.StylePriority.UseBorderWidth = true;
            ctrl.BackColor = Color.FromArgb(56, c.R, c.G, c.B);
            ctrl.StylePriority.UseBackColor = true;
        }
        catch (PlatformNotSupportedException) { }
    }

    private sealed record ControlEntry(string Id, string Path, string BandType, XRControl Control);
}
