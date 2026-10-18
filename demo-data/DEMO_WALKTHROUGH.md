# SAP General Ledger vs. Bank Statement — Demo Walkthrough & Capability Showcase

This guide provides a comprehensive scenario for demonstrating **AI-Assisted Reconciliation Optimization** using synthetic, production-grade corporate finance datasets.

---

## 1. Scenario Overview

- **Source System A (SAP ERP Financials - General Ledger)**: Account `113100` (Cash Operating Clearing Account) in Company Code `1000`. Captures GL journal entries, vendor disbursement vouchers, customer cash receipts, and payroll tax transfers.
- **Source System B (Corporate Bank Statement - MT940 / CAMT.053)**: Primary operating checking account at JPMorgan Chase. Captures electronic funds transfers (ACH), incoming/outgoing Fedwires, cleared checks, and bank charges.

---

## 2. Dataset Design & Variance Taxonomy

Both sets contain 28 transactions deliberately structured to test each capability of the deterministic engine and the AI advisor.

| Category | Count (Set 1) | Characteristics | System Capability Showcased |
| :--- | :---: | :--- | :--- |
| **1. Clean Exact Matches** | 14 pairs | Identical Reference (`INV-77001`..`INV-77014`), Amount, and Date. | Baseline exact 3-way match pass (`EXACT_REF_AMT_DATE`). 100% precision. |
| **2. Settlement Date Lag** | 4 pairs | Same Reference and Amount, but Bank Statement has a +1 to +2 day clearing lag (e.g. booked Friday in SAP, cleared Monday in Bank). | AI identifies date divergence; proposes `PARTIAL_DATE_TOL` (+/- 2 days proximity). |
| **3. Fee Deductions & Rounding** | 3 pairs | Same Reference and Date, but Bank amount reflects small wire deductions or penny rounding ($0.50, $0.75, $0.80). | AI detects small variance; proposes `PARTIAL_AMT_TOL` (+/- 1.00 tolerance). |
| **4. Typo / Format Divergence** | 3 pairs | Same Amount and Date, but Bank reference has minor typos (e.g. `INV-77O40` with letter O instead of 0, `CHK-0091Z`). | AI identifies high textual similarity (> 0.85); proposes `PARTIAL_FUZZY_REF`. |
| **5. Unilateral Month-End Items (Control Group)** | 4 records | Outstanding checks in transit (`CHK-50999`, `PAY-99100`) in SAP; bank account service charges (`SERVICE-CHG-OCT`, `INTEREST-CR-OCT`) on bank statement. | **Safety Demonstration**: AI correctly categorizes as `MissingCounterpart` and does *not* hallucinate rules. |
| **6. Ambiguity Protection** | 1 pair (2 GL, 2 Bank) | Multiple branch petty cash replenishments of identical amount ($500.00) on the same date. | The engine flags duplicate candidates and isolates ambiguity to prevent misallocation. |

---

## 3. Why Two Sets?

1. **Set 1 (October 2026)**:
   - Used for the **Initial Baseline Run** with Profile v1.
   - Initial Match Rate: **~52%** (14 of 27 matches).
   - Serves as the source run for **AI Optimization**.
   - AI proposes 3 safe additive passes, simulating a jump to **89% match rate**.
   - User commits **Profile v2**.

2. **Set 2 (November 2026)**:
   - Used for the **Verification & Production Run**.
   - Executed against the newly committed **Profile v2**.
   - Demonstrates that the newly accepted rules immediately match November's timing delays, wire fees, and typographical variations on fresh unseen data, achieving **~89% match rate on the very first run**.
   - Running an analysis on Set 2 demonstrates **Multi-Run Invariant Preservation**: the replay engine re-evaluates both October and November to verify zero regressions across multiple financial periods.

---

## 4. File Locations

- **Set 1 (October 2026)**:
  - `demo-data/set-1-october/sap_gl_october_2026.csv`
  - `demo-data/set-1-october/bank_stmt_october_2026.csv`
- **Set 2 (November 2026)**:
  - `demo-data/set-2-november/sap_gl_november_2026.csv`
  - `demo-data/set-2-november/bank_stmt_november_2026.csv`
- **Profiles & Bundles**:
  - `demo-data/sap-gl-bank-bundle.json` (Complete 1-click provisioning bundle: datasets, parsers, & profile)
  - `demo-data/profile-v1-baseline.json` (Starting baseline profile rules definition)

---

## 5. Step-by-Step Presentation Script

### Step 1: Instant 1-Click Provisioning (or Baseline Setup)
- **Option A (Instant 1-Click Provisioning — Recommended for Demos)**:
  1. Go to `/configuration` and click the **Create from JSON** card (or click **Import JSON** on `/configuration/manual/profiles`).
  2. Select `demo-data/sap-gl-bank-bundle.json` (or drag & drop / paste it).
  3. Notice how the system previews the profile (`SAP GL vs Bank Statement Reconciliation`), attached canonical datasets (`SAP_GL`, `BANK_STMT`), and parser definitions (`SAP GL Parser`, `Bank Statement Parser`).
  4. Click **Import & Open Studio** — datasets, parsers, package, and profile are created and linked in one atomic operation!
- **Option B (Manual Setup)**:
  1. Navigate to `/configuration/manual/profiles` (or `/configuration/ai`).
  2. Create or verify two datasets: `SAP_GL` and `BANK_STMT`.
  3. Create profile **"SAP GL vs Bank Statement Reconciliation"** using `profile-v1-baseline.json` and click **Approve** (Profile version 1).

### Step 2: Run Set 1 (October 2026)
1. Go to `/reconciliation/runs/new`.
2. Select the approved profile and upload `sap_gl_october_2026.csv` and `bank_stmt_october_2026.csv`.
3. Execute the run.
4. **Point out to the audience**:
   - Clean transactions matched instantly.
   - However, match rate is only **~52%** because real-world corporate banking has timing lags, wire fees, and reference variations.
   - 13 items remain open for manual reconciliation.

### Step 3: Trigger AI Optimization
1. Return to the Reconciliation Profiles list (`/configuration/manual/profiles`).
2. Point out the new **Optimization** column and click **Analyze** on the profile row.
3. Confirm the scope dialog (defaults to the October run as source).
4. Watch the progress tracker update seamlessly through *Analyzing*, *Diagnosing*, and *Replaying*.

### Step 4: Review AI Recommendations & Invariants
1. **Diagnosis Cards**:
   - Highlight the identified categories: `FormatOrToleranceDifferences` and `MissingCounterpart`.
2. **Proposed Change Cards**:
   - Card 1: `PARTIAL_DATE_TOL` (+/- 2 days date proximity).
   - Card 2: `PARTIAL_AMT_TOL` (+/- 1.00 fee / roundoff tolerance).
   - Card 3: `PARTIAL_FUZZY_REF` (fuzzy reference match with min score 0.80).
3. **Inspect Diffs**:
   - Toggle **Inspect Diff** to show exact before/after JSON rule modifications.
4. **Deterministic Simulated Impact**:
   - Match rate jumps from **52% to 89%** (+37 percentage points).
   - Highlight the **Preserved Matches: 100%** badge. Zero existing matches were regressed.
5. **AI Safety Verification**:
   - Show that the AI *refused* to force-match the month-end outstanding checks or bank maintenance fees, explaining that they are genuine unilateral timing items.
6. Click **Accept selected changes** -> Commits **Profile v2** atomically.

### Step 5: Test Fresh Unseen Data (November 2026)
1. Go to `/reconciliation/runs/new`.
2. Select the updated profile (now **version 2**).
3. Upload `sap_gl_november_2026.csv` and `bank_stmt_november_2026.csv`.
4. Execute the run.
5. **Key Takeaway**:
   - Without any manual rule tuning, the run achieves **~89% match rate on the first try**.
   - The learned rules generalize cleanly to the new accounting period.
