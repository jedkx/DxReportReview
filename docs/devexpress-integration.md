# DevExpress integration

Reports are DevExpress layout XML. The app keeps review and approve inside the normal DevExpress stack: Designer, viewer, `ReportStorageWebExtension`. The “diff” is the layout graph (`XtraReport`), not piping `.repx` through a line-oriented text diff. That is the closest you get to git-style merge without pretending XML is source code.

The review screen uses two `WebDocumentViewer` iframes. Borders come from `ReportDiffService` mutating controls before `SaveLayoutToXml`.

`IReportStorage` stores bytes. For a review, baseline and proposed deserialize, leaf controls get matched, borders get applied, then serialize again. `GetData` on the `REVIEW_*` URLs returns that payload; the viewer does not know how the diff was computed.

A raw XML line diff is noisy because of attribute order and formatting. Comparing object graphs avoids most of that noise.

Loose git analogy: one layout is like one file; leaf controls are like lines; identity leans on `Name` and path when it can, then similarity and Hungarian assignment, not Myers on XML strings.

## How it plugs in

`Program.cs` wires Skia and `AddDevExpressControls`. Thin MVC subclasses wrap the designer and viewer controllers. `ReviewAwareReportStorage` implements `GetData`, `SetData`, `SetNewData`, `GetUrls`, `IsValidUrl`, and `CanSetData`.

### Synthetic URLs

- `{id}` — normal report from `IReportStorage`.
- `REVIEW_PROPOSED_{id}` — proposed side with borders applied.
- `REVIEW_CURRENT_{id}` — baseline side with borders applied.
- `REEDIT_{id}` — raw proposed bytes from a rejected submission for re-edit.

On `SetData`, the normal path writes storage; the review path creates a `ReviewSubmission` and skips the live update. `SetNewData` follows the same split; review can use `ReportId = 0` and `IsNewReport` until someone approves.

Flow: `POST /report/set-approver`, then DevExpress issues the follow-up request; middleware copies session into `HttpContext.Items` so `SetData` can read the approver on that same request.

## ReportDiffService

Deserialize both sides, flatten leaf controls (bands act as containers).

Matching order, each control used once:

1. **Name** — only when it is unambiguous on both sides.
2. **Path** — same string from the band tree plus per-type index under each parent.
3. **Similarity** — same CLR type; weighted score (size, alignment, padding, ancestors, label text, and so on). On Linux, real font and color reads are skipped in favor of a small fixed bump so Hungarian matching does not glue unrelated labels together.
4. **Hungarian** — max-weight matching per type using [FastHungarian](https://www.nuget.org/packages/FastHungarian). If there are more baseline rows than proposed, sides swap so edges are not dropped.
5. **Same band and location** — same `BandType`, same control type, `LocationF` within a small pixel tolerance.

`GetLayoutQualityHints` is optional noise for the UI (duplicate names, etc.); it does not change `ComputeDiff`.

For controls that are already paired, “changed” uses layout properties only (`HasControlLayoutChange`: type, visibility, label text, position and size, colors on Windows, alignment). We do not mark “changed” from path or band alone; that used to paint whole bands blue when controls reordered.

Colors: green for added on the proposed side, red for removed on the baseline side, blue for changed on the proposed side, with a thick border and light tint.

Path strings look like `DetailBand[0]/XRLabel[0]`. The index is per control type under that parent, not one global child index.

### Where the diff stops being exhaustive

The viewer shows layout-level highlights on matched leaf controls. It does not prove that data sources, expressions, or scripts match.

Matching is heuristic. Name, path, similarity, and Hungarian pairing can mis-fire on odd layouts. Rich CrossTabs, subreports, or very dynamic structures are not covered by a formal guarantee.

“Changed” tracks visual and layout fields the code knows about, not a full semantic model of every DevExpress feature.

For anything that faces auditors or paying users, plan extra validation: automated tests, human review, or domain checks beyond this diff.

## Iframes

`/report/viewer-frame?url=REVIEW_CURRENT_{id}` and similar URLs are built in `ReviewController`. `_FrameLayout.cshtml` strips chrome. `_WebDocumentViewerDefaults.cshtml` sets page-width zoom (`zoom = -1`).

## Licensing

NuGet trial bits may watermark output until a license file is present on the server.
