import { Button } from "@workspace/ui/components/button"
import { useQuery } from "@tanstack/react-query"
import { Download, Table2 } from "lucide-react"
import { Link } from "react-router"
import { ErrorNote } from "@/components/error-note"
import { api } from "@/lib/api"
import { downloadCsv } from "@/lib/csv"
import type { RunSummary } from "@/lib/types"

export function RunsPage() {
  const runs = useQuery({
    queryKey: ["runs"],
    queryFn: () => api<RunSummary[]>("/api/app/run"),
  })
  const rows = runs.data ?? []
  return (
    <section className="flex flex-col gap-6">
      <div className="flex items-start justify-between gap-4">
        <div>
          <h1 className="text-2xl font-medium">Reconciliation Runs</h1>
          <p className="mt-1 text-sm text-muted-foreground">
            Recorded executions of your approved reconciliation profiles.
          </p>
        </div>
        <Button render={<Link to="/reconciliation/runs/new" />}>
          Create Run
        </Button>
      </div>
      {runs.error ? (
        <ErrorNote message={(runs.error as Error).message} />
      ) : null}
      {runs.isLoading ? (
        <p className="text-sm text-muted-foreground">Loading runs…</p>
      ) : null}
      <div className="overflow-hidden border bg-card">
        <div className="flex items-center justify-between border-b px-4 py-3">
          <p className="text-sm font-medium">{rows.length} runs</p>
          <Button
            variant="outline"
            size="sm"
            disabled={!rows.length}
            onClick={() =>
              downloadCsv(
                "reconciliation-runs.csv",
                [
                  "Run",
                  "Profile",
                  "Version",
                  "Status",
                  "Total records",
                  "Matched records",
                  "Unresolved records",
                  "Created by",
                  "Started",
                ],
                rows.map((run) => [
                  run.id,
                  run.profileName,
                  String(run.profileVersion),
                  run.status,
                  String(run.totalRecords),
                  String(run.matchedRecords),
                  String(run.unresolvedRecords),
                  run.creatorId ?? "",
                  run.creationTime,
                ])
              )
            }
          >
            <Download />
            Export
          </Button>
        </div>
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead>
              <tr className="border-b bg-muted/40 text-left text-xs text-muted-foreground">
                <th className="px-4 py-2.5">Profile</th>
                <th className="px-4 py-2.5">Status</th>
                <th className="px-4 py-2.5 text-right">Match</th>
                <th className="px-4 py-2.5 text-right">Open records</th>
                <th className="px-4 py-2.5">Created by</th>
                <th className="px-4 py-2.5">
                  <span className="sr-only">Data grid</span>
                </th>
              </tr>
            </thead>
            <tbody>
              {!runs.isLoading && !runs.error && rows.length === 0 ? (
                <tr>
                  <td
                    colSpan={6}
                    className="px-4 py-8 text-center text-muted-foreground"
                  >
                    No reconciliation runs yet.
                  </td>
                </tr>
              ) : null}
              {rows.map((run) => (
                <tr
                  key={run.id}
                  className="border-b last:border-0 hover:bg-muted/30"
                >
                  <td className="px-4 py-3">
                    <Link
                      className="font-medium underline-offset-4 hover:underline"
                      to={`/reconciliation/runs/${run.id}`}
                    >
                      {run.profileName}
                    </Link>
                    <p className="text-xs text-muted-foreground">
                      Version {run.profileVersion} ·{" "}
                      {new Date(run.creationTime).toLocaleString()}
                    </p>
                  </td>
                  <td className="px-4 py-3">{run.status}</td>
                  <td className="px-4 py-3 text-right tabular-nums">
                    {run.totalRecords
                      ? `${((run.matchedRecords / run.totalRecords) * 100).toFixed(1)}%`
                      : "—"}
                  </td>
                  <td className="px-4 py-3 text-right tabular-nums">
                    {run.unresolvedRecords.toLocaleString()}
                  </td>
                  <td className="px-4 py-3 font-mono text-xs">
                    {run.creatorId ?? "—"}
                  </td>
                  <td className="px-4 py-3 text-right">
                    <Button
                      variant="ghost"
                      size="icon-sm"
                      aria-label={`View data grid for ${run.profileName}`}
                      render={
                        <Link to={`/reconciliation/runs/${run.id}/grid`} />
                      }
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
