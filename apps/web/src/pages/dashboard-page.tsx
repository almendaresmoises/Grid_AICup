import { Button } from "@workspace/ui/components/button"
import { Tooltip } from "@workspace/ui/components/tooltip"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { ArrowUpRight, Info, Loader2, RefreshCw } from "lucide-react"
import { useState } from "react"
import { Link, useNavigate } from "react-router"
import { ErrorNote } from "@/components/error-note"
import { RunOutcomeChart } from "@/components/run-outcome-chart"
import { api } from "@/lib/api"
import type { Dashboard } from "@/lib/dashboard"
import type { RunSummary } from "@/lib/types"
import { inputClass } from "@/lib/ui"

function MatchRateInfo() {
  return (
    <Tooltip label="Match rate = (full match records + partial match records) ÷ total records × 100.">
      <button
        type="button"
        aria-label="How match rate is calculated"
        className="inline-flex rounded-full text-muted-foreground transition-colors hover:text-foreground focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
      >
        <Info className="size-3.5" aria-hidden="true" />
      </button>
    </Tooltip>
  )
}

export function DashboardPage() {
  const [period, setPeriod] = useState(30)
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const summary = useQuery({
    queryKey: ["dashboard", period],
    queryFn: ({ signal }) =>
      api<Dashboard>(`/api/app/dashboard?days=${period}`, { signal }),
    staleTime: 30_000,
    refetchOnMount: "always",
    refetchInterval: 60_000,
  })
  const data = summary.data
  const timeZone = data?.timeZone === "Etc/UTC" ? "UTC" : data?.timeZone ?? "UTC"
  const openProfileRun = useMutation({
    mutationFn: async (profileId: string) => {
      const runs = await queryClient.fetchQuery({
        queryKey: ["runs", profileId],
        queryFn: ({ signal }) =>
          api<RunSummary[]>(`/api/app/run?profileId=${profileId}`, { signal }),
        staleTime: 30_000,
      })
      const latestRun = runs
        .filter((run) =>
          run.profileId === profileId &&
          run.status.toLowerCase() === "succeeded" &&
          run.totalRecords > 0 &&
          data &&
          run.creationTime.slice(0, 10) >= data.fromDate &&
          run.creationTime.slice(0, 10) <= data.throughDate
        )
        .sort((a, b) => Date.parse(b.creationTime) - Date.parse(a.creationTime))[0]
      if (!latestRun) {
        throw new Error("No completed run is available for this profile in the selected period. Refresh the dashboard and try again.")
      }
      return latestRun.id
    },
    onSuccess: (runId) => navigate(`/reconciliation/runs/${runId}/grid`),
  })
  const stats = data
    ? [
        { label: "Completed runs", value: data.completedRuns.toLocaleString() },
        {
          label: "Records processed",
          value: data.totalRecords.toLocaleString(),
        },
        { label: "Exceptions", value: data.exceptionRecords.toLocaleString() },
        {
          label: "Match rate",
          value:
            data.matchRate === null ? "—" : `${data.matchRate.toFixed(1)}%`,
        },
      ]
    : []
  return (
    <section className="flex flex-col gap-6">
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div>
          <h1 className="text-2xl font-medium">Dashboard</h1>
          <p className="mt-1 text-sm text-muted-foreground">
            Record outcomes from completed reconciliation runs. Repeated
            executions count separately.
          </p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <label className="sr-only" htmlFor="dashboard-period">
            Reporting period
          </label>
          <select
            id="dashboard-period"
            className={`${inputClass} w-auto`}
            value={period}
            onChange={(event) => setPeriod(Number(event.target.value))}
          >
            {[7, 30, 90].map((days) => (
              <option key={days} value={days}>
                Last {days} days
              </option>
            ))}
          </select>
          <Button
            variant="outline"
            size="icon"
            disabled={summary.isFetching}
            aria-label="Refresh dashboard"
            onClick={() => void summary.refetch()}
          >
            <RefreshCw className={summary.isFetching ? "animate-spin" : ""} />
          </Button>
        </div>
      </div>
      {summary.error ? (
        <ErrorNote message={(summary.error as Error).message} />
      ) : null}
      {summary.isPending ? (
        <p className="text-sm text-muted-foreground" role="status">
          Loading reconciliation summary…
        </p>
      ) : null}
      {data ? (
        <>
          <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
            {stats.map((stat) => (
              <article
                className={`border bg-card p-5 ${stat.label === "Exceptions" ? "border-t-2 border-t-amber-500" : stat.label === "Match rate" ? "border-t-2 border-t-emerald-500" : ""}`}
                key={stat.label}
              >
                <div className="flex items-center gap-1.5 text-sm text-muted-foreground">
                  <span>{stat.label}</span>
                  {stat.label === "Match rate" ? <MatchRateInfo /> : null}
                </div>
                <p className={`mt-2 text-3xl font-medium tabular-nums ${stat.label === "Exceptions" ? "text-amber-700 dark:text-amber-400" : stat.label === "Match rate" ? "text-emerald-700 dark:text-emerald-400" : ""}`}>
                  {stat.value}
                </p>
              </article>
            ))}
          </div>
          <article className="border bg-card">
            <div className="flex flex-wrap items-start justify-between gap-2 border-b px-5 py-4">
              <div>
                <h2 className="font-medium">
                  Reconciliation outcomes over time
                </h2>
                <p className="mt-1 text-xs text-muted-foreground">
                  Daily record counts · {data.fromDate} to {data.throughDate} ·{" "}
                  {timeZone}
                </p>
              </div>
              <span className="text-xs text-muted-foreground">
                Updated {new Date(data.generatedAtUtc).toLocaleTimeString()}
              </span>
            </div>
            <div className="p-5">
              {data.completedRuns === 0 ? (
                <p className="mb-4 text-sm text-muted-foreground">
                  No completed runs in this period.{" "}
                  <Link className="underline" to="/configuration">
                    Configure a profile
                  </Link>{" "}
                  or select a different period.
                </p>
              ) : null}
              <RunOutcomeChart
                key={period}
                days={data.days}
                timeZone={timeZone}
              />
              <p className="mt-4 border-t pt-3 text-xs text-muted-foreground">
                Full match includes exact and aggregate matches. Partial match
                uses partial-rule results. Exceptions include unmatched and
                ambiguous records.
              </p>
            </div>
          </article>
          <article className="overflow-hidden border bg-card">
            <div className="border-b px-5 py-4">
              <h2 className="font-medium">
                5 profiles with the lowest match rates
              </h2>
              <p className="mt-1 text-xs text-muted-foreground">
                Matched records (full + partial) ÷ total records across
                completed runs in the selected period. Profiles without
                processed records are excluded.
              </p>
              <p className="mt-1 text-xs text-muted-foreground">
                Select a profile to view its latest completed run in this period.
              </p>
              {openProfileRun.error ? (
                <div className="mt-3">
                  <ErrorNote message={openProfileRun.error.message} />
                </div>
              ) : null}
            </div>
            <div className="overflow-x-auto">
              <table className="w-full text-sm">
                <thead>
                  <tr className="border-b bg-muted/40 text-left text-xs text-muted-foreground">
                    <th className="px-5 py-3 font-medium">Profile</th>
                    <th className="px-5 py-3 text-right font-medium">
                      <span className="inline-flex items-center gap-1.5">
                        Match rate <MatchRateInfo />
                      </span>
                    </th>
                    <th className="px-5 py-3 text-right font-medium">
                      Full match
                    </th>
                    <th className="px-5 py-3 text-right font-medium">
                      Partial match
                    </th>
                    <th className="px-5 py-3 text-right font-medium">
                      Exceptions
                    </th>
                    <th className="px-5 py-3 text-right font-medium">Runs</th>
                  </tr>
                </thead>
                <tbody>
                  {data.lowestMatchProfiles.length === 0 ? (
                    <tr>
                      <td
                        className="px-5 py-8 text-center text-muted-foreground"
                        colSpan={6}
                      >
                        No profile results in this period.
                      </td>
                    </tr>
                  ) : (
                    data.lowestMatchProfiles.map((profile) => (
                      <tr
                        key={profile.profileId}
                        className="cursor-pointer border-b transition-colors last:border-0 hover:bg-muted/50 focus-within:bg-muted/50"
                        onClick={() => {
                          if (!openProfileRun.isPending) openProfileRun.mutate(profile.profileId)
                        }}
                        aria-busy={openProfileRun.isPending && openProfileRun.variables === profile.profileId}
                      >
                        <td className="px-5 py-3">
                          <button
                            type="button"
                            className="inline-flex items-center gap-1.5 rounded-sm text-left font-medium underline-offset-4 hover:underline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring disabled:opacity-60"
                            disabled={openProfileRun.isPending}
                            title="View latest completed run"
                            aria-label={`View latest completed run for ${profile.profileName}`}
                            onClick={(event) => {
                              event.stopPropagation()
                              openProfileRun.mutate(profile.profileId)
                            }}
                          >
                            {profile.profileName}
                            {openProfileRun.isPending && openProfileRun.variables === profile.profileId ? (
                              <Loader2 className="size-3.5 animate-spin" aria-hidden="true" />
                            ) : (
                              <ArrowUpRight className="size-3.5 text-muted-foreground" aria-hidden="true" />
                            )}
                          </button>
                          <p className="mt-0.5 text-xs text-muted-foreground">
                            {profile.totalRecords.toLocaleString()} records
                          </p>
                        </td>
                        <td className="px-5 py-3 text-right font-medium tabular-nums">
                          {profile.matchRate.toFixed(1)}%
                        </td>
                        <td className="px-5 py-3 text-right tabular-nums">
                          {profile.fullMatchRecords.toLocaleString()}
                        </td>
                        <td className="px-5 py-3 text-right tabular-nums">
                          {profile.partialMatchRecords.toLocaleString()}
                        </td>
                        <td className="px-5 py-3 text-right tabular-nums">
                          {profile.exceptionRecords.toLocaleString()}
                        </td>
                        <td className="px-5 py-3 text-right tabular-nums">
                          {profile.completedRuns.toLocaleString()}
                        </td>
                      </tr>
                    ))
                  )}
                </tbody>
              </table>
            </div>
          </article>
        </>
      ) : null}
    </section>
  )
}
