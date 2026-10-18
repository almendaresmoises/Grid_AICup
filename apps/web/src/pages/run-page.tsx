import { Button } from "@workspace/ui/components/button"
import { useMutation, useQuery } from "@tanstack/react-query"
import { Loader2, Sparkles } from "lucide-react"
import { useState } from "react"
import { Link, useNavigate, useParams } from "react-router"
import { api } from "@/lib/api"
import { startOrResumeAnalysis } from "@/lib/reconciliation-analysis"
import type {
  MatchGroup,
  RunDetail,
  UnresolvedRow,
} from "@/lib/types"
import { ErrorNote } from "@/components/error-note"
import { Modal } from "@/components/modal"
import { labelClass, textareaClass } from "@/lib/ui"

export function RunPage() {
  const { runId = "" } = useParams()
  const navigate = useNavigate()
  const [context, setContext] = useState("")
  const [error, setError] = useState("")
  const [selectedGroup, setSelectedGroup] = useState<MatchGroup | null>(null)
  const [selectedUnresolved, setSelectedUnresolved] = useState<UnresolvedRow | null>(null)
  const run = useQuery({
    queryKey: ["run", runId],
    queryFn: () => api<RunDetail>(`/api/app/run/${runId}`),
  })

  const startAnalysis = useMutation({
    mutationFn: () =>
      startOrResumeAnalysis({
        requestId: crypto.randomUUID(),
        profileId: run.data?.profileId ?? "",
        sourceRunId: runId,
        context: context.trim() || undefined,
      }),
    onSuccess: (result) => {
      navigate(`/reconciliation/profiles/${run.data?.profileId}/analyses/${result.analysisId}`)
    },
    onError: (caught) =>
      setError(
        caught instanceof Error ? caught.message : "Starting optimization failed"
      ),
  })

  if (run.isLoading) {
    return <p className="text-sm">Loading run…</p>
  }
  if (run.error || !run.data) {
    return (
      <ErrorNote
        message={
          run.error instanceof Error ? run.error.message : "Run not found"
        }
      />
    )
  }

  const detail = run.data
  return (
    <section className="flex flex-col gap-6">
      <div className="flex items-start justify-between gap-4">
        <div>
          <h1 className="text-2xl font-medium">Reconciliation results</h1>
          <p className="mt-1 text-sm text-muted-foreground">
            Profile version {detail.profileVersion}
          </p>
        </div>
        <Button
          variant="outline"
          render={<Link to={`/reconciliation/profiles/${detail.profileId}`} />}
        >
          Back to profile
        </Button>
      </div>
      <div className="grid grid-cols-2 gap-3 md:grid-cols-5">
        <Stat label="Total" value={detail.totalRecords} />
        <Stat label="Matched" value={detail.matchedRecords} />
        <Stat label="Unresolved" value={detail.unresolvedRecords} />
        <Stat label="Ambiguous" value={detail.ambiguousRecords} />
        <Stat label="Groups" value={detail.matchedGroups} />
      </div>

      <div className="flex flex-col gap-3">
        <h2 className="text-lg font-medium">Match groups</h2>
        <div className="overflow-x-auto border">
          <table className="w-full text-left text-sm">
            <thead className="bg-muted">
              <tr>
                <th className="px-3 py-2">Rule</th>
                <th className="px-3 py-2">Key</th>
                <th className="px-3 py-2">Records</th>
                <th className="px-3 py-2">Evidence</th>
              </tr>
            </thead>
            <tbody>
              {detail.groups.map((group, index) => (
                <tr
                  key={index}
                  className="border-t align-top hover:bg-muted/40 cursor-pointer transition-colors"
                  onClick={() => setSelectedGroup(group)}
                  title="Click to drilldown match group"
                >
                  <td className="px-3 py-2 font-mono text-xs">{group.rule}</td>
                  <td className="px-3 py-2 font-mono text-xs">
                    {group.key.join(" · ")}
                  </td>
                  <td className="px-3 py-2 font-mono text-xs">
                    {Object.entries(group.records)
                      .map(([code, rows]) => `${code} ${rows.join(", ")}`)
                      .join(" · ")}
                  </td>
                  <td className="px-3 py-2 text-xs">
                    {group.evidence.comparison === "sum"
                      ? `sum target ${group.evidence.target} source ${group.evidence.sourceSum} variance ${group.evidence.variance}`
                      : "equal normalized keys"}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>

      <div className="flex flex-col gap-3">
        <h2 className="text-lg font-medium">Unresolved rows</h2>
        <div className="overflow-x-auto border">
          <table className="w-full text-left text-sm">
            <thead className="bg-muted">
              <tr>
                <th className="px-3 py-2">Dataset</th>
                <th className="px-3 py-2">CSV row</th>
                <th className="px-3 py-2">Reason</th>
                <th className="px-3 py-2">Record</th>
              </tr>
            </thead>
            <tbody>
              {detail.unresolved.map((row, index) => (
                <tr
                  key={index}
                  className="border-t align-top hover:bg-muted/40 cursor-pointer transition-colors"
                  onClick={() => setSelectedUnresolved(row)}
                  title="Click to drilldown unresolved row"
                >
                  <td className="px-3 py-2">{row.dataset}</td>
                  <td className="px-3 py-2">{row.csvRow}</td>
                  <td className="px-3 py-2">{row.reason}</td>
                  <td className="px-3 py-2 font-mono text-xs">
                    {Object.entries(row.record)
                      .map(([key, value]) => `${key}=${value}`)
                      .join(" · ")}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>

      {detail.unresolvedRecords > 0 ? (
        <div className="flex flex-col gap-3 border bg-card p-5">
          <div className="flex items-center gap-2">
            <Sparkles className="size-5 text-primary" />
            <h2 className="text-lg font-medium">Optimize reconciliation profile with AI</h2>
          </div>
          <p className="text-sm text-muted-foreground">
            Analyze unmatched and ambiguous records from this run to discover evidence-backed improvements.
            Candidate rules are replayed deterministically against this run and regression baselines with zero regressions.
          </p>
          <label className="flex flex-col gap-1.5">
            <span className={labelClass}>Optional business context</span>
            <textarea
              className={textareaClass}
              placeholder="Provide specific notes, date/amount tolerances, or customer-specific edge cases..."
              value={context}
              onChange={(event) => setContext(event.target.value)}
            />
          </label>
          <div>
            <Button
              disabled={startAnalysis.isPending}
              onClick={() => startAnalysis.mutate()}
            >
              {startAnalysis.isPending ? (
                <Loader2 className="size-4 animate-spin" />
              ) : (
                <Sparkles className="size-4" />
              )}
              Analyze and optimize profile
            </Button>
          </div>
          {error ? <ErrorNote message={error} /> : null}
        </div>
      ) : null}

      {/* Match Group Drilldown Modal */}
      <Modal
        isOpen={Boolean(selectedGroup)}
        onClose={() => setSelectedGroup(null)}
        title={
          selectedGroup
            ? `Match Group #${detail.groups.indexOf(selectedGroup) + 1} Drilldown`
            : ""
        }
        description={
          selectedGroup ? (
            <span className="flex flex-wrap items-center gap-x-2 gap-y-1">
              <span>Rule: <strong className="text-foreground font-mono">{selectedGroup.rule}</strong></span>
              {selectedGroup.ruleType ? <span>· Type: <strong className="text-foreground capitalize">{selectedGroup.ruleType}</strong></span> : null}
              {selectedGroup.score != null ? (
                <span className="rounded bg-primary/10 px-2 py-0.5 font-mono text-xs font-semibold text-primary">
                  Score: {Math.round(selectedGroup.score * 100)}%
                </span>
              ) : null}
            </span>
          ) : undefined
        }
        size="3xl"
        footer={
          <Button variant="outline" size="sm" onClick={() => setSelectedGroup(null)}>
            Close
          </Button>
        }
      >
        {selectedGroup ? (
          <div className="flex flex-col gap-4">
            <div>
              <h3 className="text-xs font-semibold uppercase tracking-wider text-muted-foreground mb-1.5">
                Key Values
              </h3>
              <div className="rounded border bg-muted/20 p-3 font-mono text-xs break-words">
                {selectedGroup.key.length ? selectedGroup.key.join(" · ") : "—"}
              </div>
            </div>

            <div>
              <h3 className="text-xs font-semibold uppercase tracking-wider text-muted-foreground mb-1.5">
                Matched Row Indices
              </h3>
              <div className="grid gap-2 sm:grid-cols-2">
                {Object.entries(selectedGroup.records).map(([dataset, rows]) => (
                  <div key={dataset} className="rounded border bg-muted/20 p-3">
                    <p className="text-xs font-medium text-muted-foreground">{dataset}</p>
                    <p className="mt-1 font-mono text-sm text-foreground">
                      Rows: {rows.join(", ")}
                    </p>
                  </div>
                ))}
              </div>
            </div>

            <div>
              <h3 className="text-xs font-semibold uppercase tracking-wider text-muted-foreground mb-1.5">
                Reconciliation Evidence
              </h3>
              <div className="rounded border bg-muted/10 p-3 text-xs flex flex-col gap-2">
                <div className="flex items-center gap-2">
                  <span className="text-muted-foreground">Comparison Mode:</span>
                  <span className="font-mono font-semibold text-foreground">{selectedGroup.evidence.comparison}</span>
                </div>
                {selectedGroup.evidence.comparison === "sum" ? (
                  <div className="grid gap-2 sm:grid-cols-3 mt-1">
                    <div className="rounded border bg-background p-2">
                      <span className="text-muted-foreground">Source Sum</span>
                      <p className="font-mono font-medium">{selectedGroup.evidence.sourceSum}</p>
                    </div>
                    <div className="rounded border bg-background p-2">
                      <span className="text-muted-foreground">Target</span>
                      <p className="font-mono font-medium">{selectedGroup.evidence.target}</p>
                    </div>
                    <div className="rounded border bg-background p-2">
                      <span className="text-muted-foreground">Variance</span>
                      <p className="font-mono font-medium">{selectedGroup.evidence.variance}</p>
                    </div>
                  </div>
                ) : (
                  <p className="text-muted-foreground">All configured normalized keys matched exactly.</p>
                )}
                {selectedGroup.remarks ? (
                  <div className="rounded border bg-background p-2.5 mt-1 text-foreground">
                    <span className="font-semibold text-amber-600 dark:text-amber-400">Recon Remarks: </span>
                    <span>{selectedGroup.remarks}</span>
                  </div>
                ) : null}
              </div>
            </div>
          </div>
        ) : null}
      </Modal>

      {/* Unresolved Row Drilldown Modal */}
      <Modal
        isOpen={Boolean(selectedUnresolved)}
        onClose={() => setSelectedUnresolved(null)}
        title={
          selectedUnresolved
            ? `Unresolved Row: ${selectedUnresolved.dataset} · CSV Row #${selectedUnresolved.csvRow}`
            : ""
        }
        description={
          selectedUnresolved ? (
            <span>
              Reason: <strong className="text-foreground">{selectedUnresolved.reason}</strong>
            </span>
          ) : undefined
        }
        size="3xl"
        footer={
          <Button variant="outline" size="sm" onClick={() => setSelectedUnresolved(null)}>
            Close
          </Button>
        }
      >
        {selectedUnresolved ? (
          <div>
            <h3 className="text-xs font-semibold uppercase tracking-wider text-muted-foreground mb-2.5">
              Record Fields
            </h3>
            <dl className="grid gap-3 sm:grid-cols-2 md:grid-cols-3">
              {Object.entries(selectedUnresolved.record).map(([key, value]) => (
                <div key={key} className="rounded border bg-muted/20 p-2.5">
                  <dt className="text-xs font-medium text-muted-foreground">{key}</dt>
                  <dd className="mt-1 font-mono text-xs break-words text-foreground">
                    {value || "—"}
                  </dd>
                </div>
              ))}
            </dl>
          </div>
        ) : null}
      </Modal>
    </section>
  )
}

function Stat({ label, value }: { label: string; value: number }) {
  return (
    <div className="border bg-card px-3 py-3">
      <p className="text-xs text-muted-foreground">{label}</p>
      <p className="text-2xl font-medium">{value.toLocaleString()}</p>
    </div>
  )
}
