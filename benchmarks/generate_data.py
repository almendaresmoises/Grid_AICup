import csv
import datetime
import random
from pathlib import Path

def generate_benchmark_datasets(total_rows_per_file: int = 50000, seed: int = 42):
    random.seed(seed)
    out_dir = Path("benchmarks/data")
    out_dir.mkdir(parents=True, exist_ok=True)
    
    gl_rows = []
    bank_rows = []
    
    base_date = datetime.date(2026, 10, 1)
    
    # 1. Exact matches: 32,500 pairs
    exact_count = 32500
    for i in range(exact_count):
        idx = i + 1
        ref = f"REF-{idx:07d}"
        doc_no = f"190{idx:07d}"
        stmt_no = f"ST-{idx:07d}"
        day_offset = (idx % 28)
        tx_date = (base_date + datetime.timedelta(days=day_offset)).isoformat()
        amount = f"{(10.0 + (idx % 9900) + ((idx % 100) / 100.0)):.2f}"
        comp = "US01" if (idx % 2 == 0) else "EU01"
        curr = "USD" if comp == "US01" else "EUR"
        
        gl_rows.append({
            "company_code": comp,
            "gl_account": "113100",
            "document_no": doc_no,
            "posting_date": tx_date,
            "document_date": tx_date,
            "reference": ref,
            "amount": amount,
            "currency": curr,
            "text": f"Customer settlement {ref}",
            "assignment": stmt_no,
        })
        bank_rows.append({
            "company_code": comp,
            "statement_no": stmt_no,
            "value_date": tx_date,
            "booking_date": tx_date,
            "bank_reference": ref,
            "transaction_type": "CR",
            "amount": amount,
            "currency": curr,
            "description": f"ACH CREDIT {ref}",
        })
        
    # 2. Date tolerance (offset by 2 days): 5,000 pairs
    date_tol_count = 5000
    for i in range(date_tol_count):
        idx = exact_count + i + 1
        ref = f"REF-{idx:07d}"
        doc_no = f"190{idx:07d}"
        stmt_no = f"ST-{idx:07d}"
        day_offset = (idx % 25)
        gl_date = (base_date + datetime.timedelta(days=day_offset)).isoformat()
        bank_date = (base_date + datetime.timedelta(days=day_offset + 2)).isoformat() # +2 days
        amount = f"{(25.0 + (idx % 8500) + 0.45):.2f}"
        comp = "US01" if (idx % 2 == 0) else "EU01"
        curr = "USD" if comp == "US01" else "EUR"
        
        gl_rows.append({
            "company_code": comp,
            "gl_account": "113100",
            "document_no": doc_no,
            "posting_date": gl_date,
            "document_date": gl_date,
            "reference": ref,
            "amount": amount,
            "currency": curr,
            "text": f"Vendor payment {ref}",
            "assignment": stmt_no,
        })
        bank_rows.append({
            "company_code": comp,
            "statement_no": stmt_no,
            "value_date": bank_date,
            "booking_date": bank_date,
            "bank_reference": ref,
            "transaction_type": "DR",
            "amount": amount,
            "currency": curr,
            "description": f"WIRE OUT {ref}",
        })

    # 3. Fee tolerance (minor fee of $1.50 deducted): 3,500 pairs
    fee_count = 3500
    for i in range(fee_count):
        idx = exact_count + date_tol_count + i + 1
        ref = f"REF-{idx:07d}"
        doc_no = f"190{idx:07d}"
        stmt_no = f"ST-{idx:07d}"
        day_offset = (idx % 28)
        tx_date = (base_date + datetime.timedelta(days=day_offset)).isoformat()
        base_amt = 100.0 + (idx % 4000)
        gl_amt = f"{base_amt:.2f}"
        bank_amt = f"{(base_amt - 1.50):.2f}" # $1.50 difference
        comp = "US01"
        curr = "USD"
        
        gl_rows.append({
            "company_code": comp,
            "gl_account": "113100",
            "document_no": doc_no,
            "posting_date": tx_date,
            "document_date": tx_date,
            "reference": ref,
            "amount": gl_amt,
            "currency": curr,
            "text": f"Merchant charge {ref}",
            "assignment": stmt_no,
        })
        bank_rows.append({
            "company_code": comp,
            "statement_no": stmt_no,
            "value_date": tx_date,
            "booking_date": tx_date,
            "bank_reference": ref,
            "transaction_type": "CR",
            "amount": bank_amt,
            "currency": curr,
            "description": f"POS BATCH {ref}",
        })

    # 4. Document / Statement Match: 2,500 pairs
    doc_count = 2500
    for i in range(doc_count):
        idx = exact_count + date_tol_count + fee_count + i + 1
        gl_ref = f"UNM-{idx:07d}"
        bank_ref = f"BNK-{idx:07d}"
        doc_no = f"DOC-{idx:07d}"
        day_offset = (idx % 28)
        tx_date = (base_date + datetime.timedelta(days=day_offset)).isoformat()
        amount = f"{(50.0 + (idx % 3000) + 0.10):.2f}"
        comp = "US01"
        curr = "USD"
        
        gl_rows.append({
            "company_code": comp,
            "gl_account": "113100",
            "document_no": doc_no,
            "posting_date": tx_date,
            "document_date": tx_date,
            "reference": gl_ref,
            "amount": amount,
            "currency": curr,
            "text": f"Doc reference match {doc_no}",
            "assignment": doc_no,
        })
        bank_rows.append({
            "company_code": comp,
            "statement_no": doc_no,
            "value_date": tx_date,
            "booking_date": tx_date,
            "bank_reference": bank_ref,
            "transaction_type": "CR",
            "amount": amount,
            "currency": curr,
            "description": f"SWIFT {doc_no}",
        })

    # 5. Fuzzy references: 2,000 pairs (e.g. REF-0043501 vs REF0043501)
    fuzzy_count = 2000
    for i in range(fuzzy_count):
        idx = exact_count + date_tol_count + fee_count + doc_count + i + 1
        gl_ref = f"REF-{idx:07d}"
        bank_ref = f"REF{idx:07d}" # stripped dash
        doc_no = f"190{idx:07d}"
        stmt_no = f"ST-{idx:07d}"
        day_offset = (idx % 28)
        tx_date = (base_date + datetime.timedelta(days=day_offset)).isoformat()
        amount = f"{(80.0 + (idx % 2000) + 0.75):.2f}"
        comp = "US01"
        curr = "USD"
        
        gl_rows.append({
            "company_code": comp,
            "gl_account": "113100",
            "document_no": doc_no,
            "posting_date": tx_date,
            "document_date": tx_date,
            "reference": gl_ref,
            "amount": amount,
            "currency": curr,
            "text": f"Fuzzy candidate {gl_ref}",
            "assignment": stmt_no,
        })
        bank_rows.append({
            "company_code": comp,
            "statement_no": stmt_no,
            "value_date": tx_date,
            "booking_date": tx_date,
            "bank_reference": bank_ref,
            "transaction_type": "CR",
            "amount": amount,
            "currency": curr,
            "description": f"PAYMENT {bank_ref}",
        })

    # 6. Divergent amount (FX conversion item): 1,500 pairs
    div_count = 1500
    for i in range(div_count):
        idx = exact_count + date_tol_count + fee_count + doc_count + fuzzy_count + i + 1
        ref = f"REF-{idx:07d}"
        doc_no = f"190{idx:07d}"
        stmt_no = f"ST-{idx:07d}"
        day_offset = (idx % 28)
        tx_date = (base_date + datetime.timedelta(days=day_offset)).isoformat()
        gl_amt = f"{(500.0 + (idx % 1000) + 0.30):.2f}"
        bank_amt = f"{(530.0 + (idx % 1000) + 0.30):.2f}" # FX variation
        comp = "EU01"
        curr = "EUR"
        
        gl_rows.append({
            "company_code": comp,
            "gl_account": "113100",
            "document_no": doc_no,
            "posting_date": tx_date,
            "document_date": tx_date,
            "reference": ref,
            "amount": gl_amt,
            "currency": curr,
            "text": f"FX transaction {ref}",
            "assignment": stmt_no,
        })
        bank_rows.append({
            "company_code": comp,
            "statement_no": stmt_no,
            "value_date": tx_date,
            "booking_date": tx_date,
            "bank_reference": ref,
            "transaction_type": "CR",
            "amount": bank_amt,
            "currency": curr,
            "description": f"FX INWARD {ref}",
        })

    # 7. Unmatched items: remaining rows to reach exactly total_rows_per_file (3,000 items)
    current_count = len(gl_rows)
    unmatched_count = total_rows_per_file - current_count
    for i in range(unmatched_count):
        idx = current_count + i + 1
        gl_ref = f"GL-ONLY-{idx:07d}"
        bank_ref = f"BNK-ONLY-{idx:07d}"
        tx_date = (base_date + datetime.timedelta(days=(idx % 28))).isoformat()
        amount_gl = f"{(15.0 + (idx % 500) + 0.99):.2f}"
        amount_bnk = f"{(90.0 + (idx % 700) + 0.88):.2f}"
        
        gl_rows.append({
            "company_code": "US01",
            "gl_account": "113100",
            "document_no": f"GLDOC-{idx:07d}",
            "posting_date": tx_date,
            "document_date": tx_date,
            "reference": gl_ref,
            "amount": amount_gl,
            "currency": "USD",
            "text": f"Unmatched GL transaction {gl_ref}",
            "assignment": f"ASN-{idx:07d}",
        })
        bank_rows.append({
            "company_code": "US01",
            "statement_no": f"BNKST-{idx:07d}",
            "value_date": tx_date,
            "booking_date": tx_date,
            "bank_reference": bank_ref,
            "transaction_type": "DR",
            "amount": amount_bnk,
            "currency": "USD",
            "description": f"Unmatched Bank fee {bank_ref}",
        })

    # Shuffle datasets independently while maintaining deterministic order
    random.shuffle(gl_rows)
    random.shuffle(bank_rows)

    # Write SAP GL CSV
    gl_file = out_dir / "benchmark_sap_gl_50k.csv"
    gl_keys = ["company_code", "gl_account", "document_no", "posting_date", "document_date", "reference", "amount", "currency", "text", "assignment"]
    with open(gl_file, "w", newline="", encoding="utf-8") as f:
        writer = csv.DictWriter(f, fieldnames=gl_keys)
        writer.writeheader()
        writer.writerows(gl_rows)

    # Write Bank Statement CSV
    bank_file = out_dir / "benchmark_bank_stmt_50k.csv"
    bank_keys = ["company_code", "statement_no", "value_date", "booking_date", "bank_reference", "transaction_type", "amount", "currency", "description"]
    with open(bank_file, "w", newline="", encoding="utf-8") as f:
        writer = csv.DictWriter(f, fieldnames=bank_keys)
        writer.writeheader()
        writer.writerows(bank_rows)

    print(f"Generated {len(gl_rows):,} SAP GL rows -> {gl_file} ({gl_file.stat().st_size / (1024*1024):.2f} MB)")
    print(f"Generated {len(bank_rows):,} Bank Statement rows -> {bank_file} ({bank_file.stat().st_size / (1024*1024):.2f} MB)")
    print(f"Total Benchmark Transactions: {len(gl_rows) + len(bank_rows):,} rows")

if __name__ == "__main__":
    generate_benchmark_datasets(50000)
