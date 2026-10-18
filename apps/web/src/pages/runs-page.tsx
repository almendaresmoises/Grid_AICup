import { Button } from "@workspace/ui/components/button"
import { useQueries, useQuery } from "@tanstack/react-query"
import { Download, Table2 } from "lucide-react"
import { Link } from "react-router"
import { ErrorNote } from "@/components/error-note"
import { api } from "@/lib/api"
import { downloadCsv } from "@/lib/csv"
import type { RunSummary } from "@/lib/types"
import {
  getCurrentUser,
  shortUserId,
  userDisplayName,
  type UserIdentity,
} from "@/lib/user-identity"

const statusColors: Record<string, string> = {
  succeeded:
    "border-emerald-200 bg-emerald-50 text-emerald-700 dark:border-emerald-500/30 dark:bg-emerald-500/10 dark:text-emerald-400",
  failed:
    "border-red-200 bg-red-50 text-red-700 dark:border-red-500/30 dark:bg-red-500/10 dark:text-red-400",
  running:
    "border-amber-200 bg-amber-50 text-amber-700 dark:border-amber-500/30 dark:bg-amber-500/10 dark:text-amber-400",
}

export function RunsPage() {
  const runs = useQuery({
    queryKey: ["runs"],
    queryFn: () => api<RunSummary[]>("/api/app/run"),
  })
  const rows = runs.data ?? []
  const currentUser = useQuery({
    queryKey: ["current-user"],
    queryFn: getCurrentUser,
    staleTime: 5 * 60 * 1000,
    retry: 1,
  })
  const currentUserId = currentUser.data?.id?.toLowerCase()
  const otherCreatorIds = [...new Set(rows.flatMap((run) =>
    run.creatorId && run.creatorId.toLowerCase() !== currentUserId
      ? [run.creatorId.toLowerCase()]
      : []
  ))]
  const creatorQueries = useQueries({
    queries: otherCreatorIds.map((id) => ({
      queryKey: ["run-creator", currentUserId, id],
      queryFn: ({ signal }: { signal: AbortSignal }) =>
        api<UserIdentity>(`/api/identity/users/${id}`, { signal }),
      enabled: !currentUser.isPending,
      staleTime: 5 * 60 * 1000,
      retry: false,
    })),
  })
  const creators = new Map(
    creatorQueries.flatMap((query, index) =>
      query.data?.id?.toLowerCase() === otherCreatorIds[index]
        ? [[otherCreatorIds[index], query.data] as const]
        : []
    )
  )

  function creatorName(id: string | null) {
    const user = id && currentUserId && id.toLowerCase() === currentUserId
      ? currentUser.data
      : creators.get(id?.toLowerCase() ?? "")
    return userDisplayName(user) ?? shortUserId(id)
  }

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
          <p className="text-sm font-medium">
            {rows.length} {rows.length === 1 ? "run" : "runs"}
          </p>
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
                  <span className="sr-only">View details</span>
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
                  className="border-b transition-colors last:border-0 hover:bg-muted/50 focus-within:bg-muted/50"
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
                  <td className="px-4 py-3">
                    <span
                      className={`inline-flex items-center rounded-full border px-2 py-0.5 text-xs font-medium ${statusColors[run.status.toLowerCase()] ?? "border-border bg-muted text-muted-foreground"}`}
                    >
                      {run.status}
                    </span>
                  </td>
                  <td className="px-4 py-3 text-right tabular-nums">
                    {run.totalRecords
                      ? `${((run.matchedRecords / run.totalRecords) * 100).toFixed(1)}%`
                      : "—"}
                  </td>
                  <td className="px-4 py-3 text-right tabular-nums">
                    {run.unresolvedRecords.toLocaleString()}
                  </td>
                  <td className="px-4 py-3 text-sm" title={run.creatorId ?? undefined}>
                    {creatorName(run.creatorId)}
                  </td>
                  <td className="px-4 py-3 text-right">
                    <Button
                      variant="ghost"
                      size="sm"
                      aria-label={`View details for ${run.profileName}`}
                      title="View details"
                      render={
                        <Link to={`/reconciliation/runs/${run.id}/grid`} />
                      }
                    >
                      <Table2 />
                      <span>View details</span>
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
