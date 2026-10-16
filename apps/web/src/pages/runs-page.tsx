import { Button } from "@workspace/ui/components/button"
import { Download, Table2 } from "lucide-react"
import { Link } from "react-router"
import { downloadCsv } from "@/lib/csv"
import { sampleRuns, type RunStatus } from "@/lib/sample-runs"

const statusTone: Record<RunStatus, string> = {
  Success: "bg-primary",
  Ongoing: "bg-foreground/40",
  Failed: "bg-destructive",
}

export function RunsPage() {
  return (
    <section className="flex flex-col gap-6">
      <div className="flex items-start justify-between gap-4">
        <div>
          <h1 className="text-2xl font-medium">Reconciliation Runs</h1>
          <p className="text-muted-foreground mt-1 text-sm">Each run uses one profile and reports how much of the batch matched.</p>
        </div>
        <Button type="button">Create Run</Button>
      </div>

      <div className="overflow-hidden border bg-card">
        <div className="flex items-center justify-between border-b px-4 py-3">
          <p className="text-sm font-medium">{sampleRuns.length} runs</p>
          <Button type="button" variant="outline" size="sm" onClick={exportRuns}>
            <Download />
            Export
          </Button>
        </div>
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead>
              <tr className="text-muted-foreground bg-muted/40 border-b text-left text-xs">
                <th className="px-4 py-2.5 font-medium">Profile</th>
                <th className="px-4 py-2.5 font-medium">Status</th>
                <th className="px-4 py-2.5 text-right font-medium">Match</th>
                <th className="px-4 py-2.5 text-right font-medium">Open items</th>
                <th className="px-4 py-2.5 font-medium">Ran by</th>
                <th className="px-4 py-2.5 text-right font-medium">
                  <span className="sr-only">Data grid</span>
                </th>
              </tr>
            </thead>
            <tbody>
              {sampleRuns.map((run) => (
                <tr key={run.id} className="hover:bg-muted/30 border-b last:border-0">
                  <td className="px-4 py-3">
                    <p className="font-medium">{run.profile}</p>
                    <p className="text-muted-foreground text-xs">{run.started}</p>
                  </td>
                  <td className="px-4 py-3">
                    <span className="inline-flex items-center gap-2">
                      <span className={`size-1.5 ${statusTone[run.status]}`} />
                      {run.status}
                    </span>
                  </td>
                  <td className="px-4 py-3 text-right tabular-nums">{run.matchPercent == null ? "—" : `${run.matchPercent.toFixed(1)}%`}</td>
                  <td className="px-4 py-3 text-right tabular-nums">{run.openItems == null ? "—" : run.openItems.toLocaleString("en-US")}</td>
                  <td className="px-4 py-3">{run.ranBy}</td>
                  <td className="px-4 py-3 text-right">
                    <Button
                      variant="ghost"
                      size="icon-sm"
                      aria-label={`View data grid for ${run.profile}`}
                      title="View data grid"
                      render={<Link to={`/reconciliation/runs/${run.id}/grid`} />}
                    >
                      <Table2 />
                    </Button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>
    </section>
  )
}

function exportRuns() {
  downloadCsv(
    "reconciliation-runs.csv",
    ["Profile", "Status", "Match", "Open items", "Ran by", "Started"],
    sampleRuns.map((run) => [
      run.profile,
      run.status,
      run.matchPercent == null ? "" : `${run.matchPercent.toFixed(1)}%`,
      run.openItems == null ? "" : String(run.openItems),
      run.ranBy,
      run.started,
    ]),
  )
}
