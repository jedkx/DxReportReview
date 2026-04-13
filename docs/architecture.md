# Architecture

This project is a wiring sketch, not something you deploy to customers as-is. The README calls out what is demo fluff versus what is worth studying; this page is just the map of how the pieces talk to each other.

The idea is merge-request style review on DevExpress layouts: diff the report object graph, show adds and removes and edits, then copy approved bytes into storage. That is not the same as running `git diff` on XML — same general workflow as a code host, different engine.

Controllers and Razor sit on top. `Domain` and `ReviewRules` sit in the middle with no I/O. Storage goes through `IReportStorage` and `IReviewRepository`. DevExpress only ever sees your app through `ReviewAwareReportStorage`, which subclasses `ReportStorageWebExtension`.

Domain types are `ReviewSubmission`, `ReviewStatus`, and helpers in `ReviewRules`.

`IReportStorage` holds layout bytes and report names. `IReviewRepository` holds submissions. The implementations checked in are in-memory and meant for local play; you would replace them with something durable in a real system without rewriting the rules from scratch if you keep the interfaces.

`ReviewAwareReportStorage` handles designer and viewer URLs plus the fake `REVIEW_*` keys. Diffing uses deserialized `XtraReport` instances, not raw XML strings. Finer detail lives in [devexpress-integration.md](devexpress-integration.md).

Infrastructure here means cookie auth and `ReviewModeMiddleware`, which copies session into `HttpContext.Items` for the same HTTP request as DevExpress `SetData` so the designer save path can see who the approver is.

Web controllers: `ReportController` for designer, viewer, and set-approver; `ReviewController` for the queue and approve or reject; `AuthController` and `HomeController` for navigation.

When someone submits for review, the designer save path opens the modal, session stores the approver choice, DevExpress calls `SetData`, and a `ReviewSubmission` appears while the live report stays untouched until approval.

Approve is a POST to `/review/{id}/approve` with conflict checks, then proposed bytes go into `IReportStorage` when the rules allow it.

After a reject, re-edit opens the designer with `REEDIT_{id}`; `GetData` serves the rejected proposed bytes and a new submission can point at `ParentReviewId`.
