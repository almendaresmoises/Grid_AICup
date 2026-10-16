import { Button } from "@workspace/ui/components/button"
import { ArrowLeft, Download, Eye } from "lucide-react"
import { useState } from "react"
import { Link, useParams } from "react-router"
import { downloadCsv } from "@/lib/csv"
import { findSampleRun, gridForRun, type GridRow } from "@/lib/sample-runs"

const statusTone = {
  Matched: "bg-primary",
  Open: "bg-foreground/40",
  Ambiguous: "bg-destructive",
}

export function RunGridPage() {
  const { runId = "" } = useParams()
  const run = findSampleRun(runId)
  const rows = gridForRun(runId)
  const [selectedKey, setSelectedKey] = useState<string | null>(null)
  const selected = rows.find((row) => rowKey(row) === selectedKey) ?? null

  if (!run) {
    return (
      <section className="flex flex-col gap-4">
        <div className="flex justify-end">
          <BackLink />
        </div>
        <h1 className="text-2xl font-medium">Run not found</h1>
      </section>
    )
  }

  return (
    <section className="flex flex-col gap-6">
      <div className="flex items-start justify-between gap-4">
        <div>
          <p className="text-muted-foreground text-sm">Drill-down of Reconciliation Runs</p>
          <h1 className="mt-1 text-2xl font-medium">{run.profile}</h1>
          <p className="text-muted-foreground mt-1 text-sm">
            Opened from run {run.id}
            {` · ${run.status}`}
            {run.matchPercent == null ? "" : ` · ${run.matchPercent.toFixed(1)}% match`}
            {run.openItems == null ? "" : ` · ${run.openItems.toLocaleString("en-US")} open items`}
            {` · ${run.ranBy} · ${run.started}`}
          </p>
        </div>
        <BackLink />
      </div>

      {run.status === "Failed" ? (
        <p className="border bg-card px-4 py-3 text-sm">
          This run failed before a grid was written. The source file for card settlement could not be read.
        </p>
      ) : null}

      {run.status === "Ongoing" ? (
        <p className="border bg-card px-4 py-3 text-sm">Rows below are the records posted so far. The rest of the batch is still matching.</p>
      ) : null}

      <div className="overflow-hidden border bg-card">
        <div className="flex items-center justify-between border-b px-4 py-3">
          <p className="text-sm font-medium">{rows.length.toLocaleString("en-US")} rows</p>
          <Button type="button" variant="outline" size="sm" disabled={rows.length === 0} onClick={() => exportGrid(run.id, rows)}>
            <Download />
            Export
          </Button>
        </div>
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead>
              <tr className="text-muted-foreground bg-muted/40 border-b text-left text-xs">
                <th className="px-4 py-2.5 font-medium">Group</th>
                <th className="px-4 py-2.5 font-medium">Status</th>
                <th className="px-4 py-2.5 font-medium">Dataset</th>
                <th className="px-4 py-2.5 font-medium">Reference</th>
                <th className="px-4 py-2.5 text-right font-medium">Amount</th>
                <th className="px-4 py-2.5 font-medium">Date</th>
                <th className="px-4 py-2.5 font-medium">Variance</th>
                <th className="px-4 py-2.5 text-right font-medium">
                  <span className="sr-only">View details</span>
                </th>
              </tr>
            </thead>
            <tbody>
              {rows.length === 0 ? (
                <tr>
                  <td className="text-muted-foreground px-4 py-8 text-center" colSpan={8}>
                    No rows in this grid.
                  </td>
                </tr>
              ) : (
                rows.map((row) => {
                  const key = rowKey(row)
                  const open = key === selectedKey
                  return (
                    <tr key={key} className={`border-b last:border-0 ${open ? "bg-muted/50" : "hover:bg-muted/30"}`}>
                      <td className="px-4 py-2.5 font-mono text-xs">{row.group}</td>
                      <td className="px-4 py-2.5">
                        <span className="inline-flex items-center gap-2">
                          <span className={`size-1.5 ${statusTone[row.status]}`} />
                          {row.status}
                        </span>
                      </td>
                      <td className="px-4 py-2.5">{row.dataset}</td>
                      <td className="px-4 py-2.5 font-mono text-xs">{row.reference}</td>
                      <td className="px-4 py-2.5 text-right tabular-nums">{money(row.amount)}</td>
                      <td className="text-muted-foreground px-4 py-2.5">{row.date}</td>
                      <td className="px-4 py-2.5">{row.variance}</td>
                      <td className="px-4 py-2.5 text-right">
                        <Button
                          type="button"
                          variant={open ? "secondary" : "ghost"}
                          size="icon-sm"
                          aria-pressed={open}
                          aria-label={open ? `Hide details for ${row.reference}` : `View details for ${row.reference}`}
                          title={open ? "Hide details" : "View details"}
                          onClick={() => setSelectedKey(open ? null : key)}
                        >
                          <Eye />
                        </Button>
                      </td>
                    </tr>
                  )
                })
              )}
            </tbody>
          </table>
        </div>
      </div>

      {selected ? <RowDetails row={selected} profile={run.profile} /> : null}
    </section>
  )
}

function BackLink() {
  return (
    <Button variant="outline" size="sm" render={<Link to="/reconciliation/runs" />}>
      <ArrowLeft />
      Back to recon run
    </Button>
  )
}

function RowDetails({ row, profile }: { row: GridRow; profile: string }) {
  const fields = [
    ["Profile", profile],
    ["Group", row.group],
    ["Status", row.status],
    ["Dataset", row.dataset],
    ["Reference", row.reference],
    ["Amount", money(row.amount)],
    ["Date", row.date],
    ["Variance", row.variance],
  ]
  return (
    <article className="border bg-card p-5">
      <h2 className="font-medium">
        {row.reference}
        <span className="text-muted-foreground font-normal"> · {row.dataset}</span>
      </h2>
      <dl className="mt-4 grid gap-x-8 gap-y-3 sm:grid-cols-4">
        {fields.map(([label, value]) => (
          <div key={label}>
            <dt className="text-muted-foreground text-xs">{label}</dt>
            <dd className="mt-0.5 text-sm">{value}</dd>
          </div>
        ))}
      </dl>
    </article>
  )
}

function exportGrid(runId: string, rows: GridRow[]) {
  downloadCsv(
    `${runId}-grid.csv`,
    ["Group", "Status", "Dataset", "Reference", "Amount", "Date", "Variance"],
    rows.map((row) => [row.group, row.status, row.dataset, row.reference, money(row.amount), row.date, row.variance]),
  )
}

function rowKey(row: GridRow) {
  return `${row.dataset}-${row.reference}`
}

function money(amount: number) {
  return new Intl.NumberFormat("en-US", { style: "currency", currency: "USD", maximumFractionDigits: 0 }).format(amount)
}
