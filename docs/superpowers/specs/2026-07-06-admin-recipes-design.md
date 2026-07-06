# BakedManila — Admin Recipes with Batch Calculator — Design Spec

**Date:** 2026-07-06
**Status:** Approved pending final review
**Repos:** `bakedmanila-api` (recipe entities + admin endpoints), `bakedmanila-web` (admin Recipes tab)

---

## 1. Overview

A new **Recipes** section in the admin panel. The baker stores each recipe with its per-batch yield (e.g. "chocolate chip → 8 pieces per batch") and its ingredient list, then uses a **batch calculator**: enter how many pieces (or batches) are needed and see the scaled ingredient amounts.

This is an internal admin tool. Recipes are never exposed to customers. A recipe may **optionally link to one product** in the catalog (dropdown in the recipe form); the link is informational — the detail page shows the linked product — with no computed behavior. Recipes for items not currently on the rotating menu simply stay unlinked.

### Requirements (from brainstorming, 2026-07-06)

- Recipes persist in the database via the API (available from any device the baker logs in on).
- One yield per recipe (no size variants — minis are a separate recipe entry).
- Calculator accepts **pieces needed** or **batches**; pieces are converted by **rounding up to whole batches** (`ceil(pieces ÷ yieldPerBatch)`). Ingredients scale by whole batch count only — no fractional batches.
- Recipe stores a free-text **method/notes** field written in **markdown** (long instructions, paragraphs, lists, bold).

### Out of scope

Fractional/exact scaling, size-variant yields, order-demand production planning (computing batches from pending orders), ingredient inventory or costing, recipe photos, unit conversion (g ↔ cups — quantities scale numerically, units are labels).

---

## 2. Data model (`bakedmanila-api`)

Two new entities in `BakedManila.Core`, persisted via EF Core migration:

```
Recipe
├── Id              int, PK
├── Name            string, required, max 100
├── YieldPerBatch   int, required, > 0        (pieces produced by one batch)
├── Notes           string?, max 8000          (markdown method/instructions)
├── ProductId       int?, FK → Product         (optional link; ON DELETE SET NULL)
└── Ingredients     owned collection, ordered by SortOrder

RecipeIngredient
├── Id        int, PK
├── RecipeId  int, FK → Recipe (cascade delete)
├── Name      string, required, max 100
├── Quantity  decimal(9,2), required, > 0
├── Unit      string?, max 20                  (free-text label: "g", "cups"; null for countables like eggs)
└── SortOrder int                              (preserves the order the baker entered)
```

Decimal for `Quantity` per `code-style.md`. `Notes` stores raw markdown — the API treats it as opaque text.

---

## 3. API endpoints (`bakedmanila-api`)

All under the existing JWT-protected admin group, following the current admin controller patterns:

| Endpoint | Behavior |
| --- | --- |
| `GET /api/admin/recipes` | List all recipes (id, name, yieldPerBatch, ingredient count) |
| `GET /api/admin/recipes/{id}` | Full recipe with ordered ingredients and notes; 404 if missing |
| `POST /api/admin/recipes` | Create recipe with ingredients; 201 + created body |
| `PUT /api/admin/recipes/{id}` | **Full replace**, including the ingredient collection (delete-and-recreate ingredients is acceptable at this scale); 404 if missing |
| `DELETE /api/admin/recipes/{id}` | Delete recipe + ingredients; 204; 404 if missing |

- DTOs are records with validation attributes on **constructor parameters** (never `[property:]` — known net10 MVC gotcha).
- Recipe DTOs carry `productId` (nullable); GET responses also include the linked product's name so the UI needn't join client-side. POST/PUT reject a `productId` that doesn't exist (400).
- **No scaling endpoint** — scaling is pure client-side arithmetic.
- Validation failures return the standard problem-details shape.

---

## 4. Web UI (`bakedmanila-web`)

Two new lazy-loaded admin routes plus a nav entry in the admin layout, mirroring the existing products page patterns (TanStack Query, dual-mode modal, RHF + zod):

### `/admin/recipes` — list

- Card per recipe: name + "8 per batch", tap to open detail.
- **Add** button opens the recipe modal (create mode).

### Recipe modal (create/edit)

- Fields: name, yield per batch, **linked product** (optional dropdown fed by the existing admin products query, with a "None" choice), dynamic ingredient rows (name, quantity, unit — rows can be added and removed; their order in the form is the stored `SortOrder`; no drag-to-reorder), notes textarea (raw markdown, no preview).
- RHF + zod validation mirroring API rules; API problem-details surfaced on submit failure.

### `/admin/recipes/:id` — detail with calculator

- **Calculator** at top: numeric input with a pieces/batches toggle.
  - Pieces mode: shows `ceil(pieces ÷ yieldPerBatch)` batches and total pieces produced (e.g. "24 pieces from 3 batches").
  - Batches mode: uses the entered count directly.
  - Defaults to 1 batch; state is local (not persisted).
- **Ingredient list** below: each quantity × batch count, formatted without trailing zeros ("2.5" not "2.50", "750" not "750.00"). Units displayed as stored.
- **Linked product**: when set, shown as a small badge/line ("Linked to: Chocolate Chip Cookies"); absent when unlinked.
- **Method**: notes rendered with `react-markdown` (GFM lists/bold/headings; raw HTML is not rendered — safe by default).
- Edit (opens modal in edit mode) and Delete (with confirm) actions.

Mutations invalidate the recipe queries. Missing recipe id → existing not-found treatment.

---

## 5. Testing

- **API**: endpoint tests in the existing suite — auth required (401 without token), CRUD round-trips, PUT full-replace semantics (ingredients removed/added correctly), validation rejections, 404s, nonexistent `productId` rejected, deleting a linked product nulls the recipe link.
- **Web**: component tests per page/modal; **calculator unit tests** covering exact division, round-up (20 ÷ 8 → 3), batches mode, and quantity formatting; **wire-contract-pinning tests** for the new endpoints (method, URL, body shape) per the admin-panel lesson; Storybook stories for the new components per `web-components.md`.

---

## 6. Delivery

Two PRs, API first:

1. `bakedmanila-api` — `feat/admin-recipes`: entities, migration, endpoints, tests. Includes this spec.
2. `bakedmanila-web` — `feat/admin-recipes`: routes, pages, modal, calculator, `react-markdown` dependency, tests + stories.

Both branch from `main`; squash-merge with green CI per `git-workflow.md`.
