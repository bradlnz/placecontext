# Australian grocery price comparison

Enter a product name, such as **Sanitarium Weet-Bix Original 575g**. The workflow
defaults to postcode **4101**; you can change it for each run.

It opens Woolworths and Coles in separate disposable QEMU desktops, uses the Rust
Jev browser agent to inspect screenshots of each offer, and produces an HTML comparison with
product names, pack sizes, item prices, unit prices, timestamps and source links.
It never adds to a cart or places an order.

## Workflow

1. **Product and postcode** validates the input.
2. **QEMU: Woolworths** searches, selects the location when possible and inspects a product.
3. **Assess Woolworths evidence** accepts a candidate price only when Jev's independent
   screenshot checks pass for price, pack size, product match, selected postcode and availability.
   HTML text is never accepted as price evidence. Each offer retains its replay frame and image hash.
4. **QEMU: Coles** repeats the inspection in a fresh desktop.
5. **Assess Coles evidence** applies the same checks.
6. **Jev comparability review** checks that both offers describe the same brand and variety.
7. **Compare verified offers** performs decimal arithmetic, without model-generated numbers.
8. **Price comparison report** creates an HTML artifact.

Equal-sized packs are compared by item price. Different-sized packs are compared
by price per kg, litre or item, with the pack totals also shown. Multipacks such
as `6 x 250ml` are treated as 1.5 litres. This follows the purpose of
[unit pricing](https://www.accc.gov.au/business/industry-codes/unit-pricing-code):
comparison using a common measure.

If either price, product match, location or availability is uncertain, the report
does **not** declare a cheapest option. Different brands are not silently treated
as equivalent. Member-only and multibuy-only prices are excluded. The output is
the lowest of the two verified observations, not a claim to have searched all
Australian retailers or every available pack.

## Install

1. Configure the [QEMU Jev runner](../../deploy/qemu/README.md), including an image-capable
   model for navigation and reading candidate values. Use the `browser-linux` image profile,
   `Executor=jev`, and allow guest networking. Set `Agent:MaxSteps` to 60 for these
   longer research tasks. Each browser step has a 600-second deadline.
2. Generate an import containing only this project's jobs and workflow:

   ```bash
   python3 examples/grocery-compare/build.py \
     --output /tmp/grocery-workflow.json
   ```

   The import uses blank/null settings, which the existing importer leaves
   unchanged. It contains no secrets or unrelated workspace definitions. The
   dedicated project path is `/demos/everyday-research`; reimporting updates these
   demo definitions by name, so export any edits you want to preserve first.
3. Import the generated file using **Settings → Backup**.
4. Configure `TYPESAFE_API_KEY` on the host for screenshot judgments and the built-in Jev review.
   The Python assessment jobs validate the recorded judgments locally and need no project API key
   or network access. TypeSafe image input is experimental and enabled per request.
5. Run **Australian grocery price comparison** and enter the product name. The
   postcode defaults to `4101` when left blank. JSON input also works:

   ```json
   {"product_name":"Sanitarium Weet-Bix Original 575g","postcode":"4101"}
   ```

Python jobs reuse the platform's existing runtime for parsing and reports; browser
execution remains the Rust agent. No additional browser libraries are installed.
Artifact downloads need the platform's configured artifact storage. The comparison
job's JSON is also visible in its run output, and QEMU screenshots are in replay.

## Demonstrate useful outcomes

| Input | What to show |
| --- | --- |
| A named product with a pack size | Matching packs, current prices and a source link for each retailer |
| A named product without a size | Different pack totals versus normalized unit value |
| A product missing from one retailer | An incomplete comparison, rather than a fabricated winner |

These are live observations, so results are not hardcoded. Pricing assessment uses
Jev's [image inputs and independent yes/no questions](https://docs.typesafe.ai/images),
with a 0.85 threshold. Confidence is a gate, not proof; inspect the evidence and
retailer link before relying on a result.

## Checks and current limits

```bash
python3 -m unittest discover -s examples/grocery-compare -v
PLACECONTEXT_TEST_GROCERY_MANIFEST=/tmp/grocery-workflow.json \
  dotnet test tests/PlaceContext.Application.Tests --filter FullyQualifiedName~GroceryWorkflowManifest
```

Tests use clearly synthetic offers and scripted Jev answers; they do not assert
today's prices. Retailer login, location selectors, access challenges or unsupported
browser controls may prevent a verified offer. An explicitly observed unavailable
page can produce an incomplete report; a VM/provider failure or exhausted action
budget stops the workflow and requires retrying. No challenge bypass is attempted.
Only quantities expressed in g, kg, ml, L or numeric pack counts are currently
normalized. Unclear pack sizes are left unverified. Delivery fees are not compared.

On 30 October 2026 the live screenshot-driven QEMU run searched for Sanitarium Weet-Bix
Original 575g and reached Woolworths' 4101 pickup selector. The screenshot showed West Village
SM, West End QLD 4101 and a **Save & login** button. The runner stopped without logging in.
Jev rejected the absent price and pack evidence; no verified cheapest result was produced.
The larger replay, full-screen mode, animated pointer targets and click highlights were checked
in Chromium. A separate real-QEMU fixture verified coordinate form entry and submission.
These are integration checks, not a broad evaluation of experimental image-model accuracy.
