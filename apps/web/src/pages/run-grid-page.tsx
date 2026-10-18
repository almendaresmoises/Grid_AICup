import { Button } from "@workspace/ui/components/button"
import { useQuery } from "@tanstack/react-query"
import {
  ChevronLeft,
  ChevronRight,
  ChevronsLeft,
  ChevronsRight,
  Download,
  Eye,
  Filter,
  Search,
  X,
} from "lucide-react"
import { useMemo, useState } from "react"
import { Link, useParams } from "react-router"
import { ErrorNote } from "@/components/error-note"
import { Modal } from "@/components/modal"
import { api } from "@/lib/api"
import { downloadCsv } from "@/lib/csv"
import type { Evidence, RunDetail } from "@/lib/types"
import { inputClass } from "@/lib/ui"

type GridRow = {
  id: string
  dataset: string
  csvRow: number
  status: "Matched" | "Partial Match" | "Unmatched" | "Ambiguous"
  rule: string
  group: string
  record: Record<string, string>
  evidence?: Evidence
  remarks?: string
  score?: number
}

export function RunGridPage() {
  const { runId = "" } = useParams()
  const result = useQuery({
    queryKey: ["run", runId],
    queryFn: () => api<RunDetail>(`/api/app/run/${runId}`),
  })

  const [selectedId, setSelectedId] = useState<string | null>(null)
  const [searchTerm, setSearchTerm] = useState("")
  const [statusFilter, setStatusFilter] = useState<string>("All")
  const [datasetFilter, setDatasetFilter] = useState<string>("All")
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(50)
  const [jumpPageInput, setJumpPageInput] = useState("")

  const run = result.data

  const allRows = useMemo<GridRow[]>(() => {
    if (!run) return []
    const items: GridRow[] = run.groups.flatMap((group, index) => {
      const isPartial =
        group.ruleType === "partial" ||
        group.evidence?.comparison === "partial_match"
      const status = isPartial ? ("Partial Match" as const) : ("Matched" as const)
      const remarks =
        group.remarks ||
        group.evidence?.remarks ||
        (isPartial ? "Partial match on subset of keys" : "Exact match on all keys")
      const score =
        group.score ?? group.evidence?.score ?? (status === "Matched" ? 1.0 : undefined)

      return Object.entries(group.records).flatMap(([dataset, numbers]) =>
        numbers.map((csvRow) => ({
          id: `${dataset}-${csvRow}`,
          dataset,
          csvRow,
          status,
          rule: group.rule,
          group: String(index + 1),
          record: run.datasets[dataset]?.[csvRow - 2] ?? {},
          evidence: group.evidence,
          remarks,
          score,
        }))
      )
    })
    items.push(
      ...run.unresolved.map((row) => ({
        id: `${row.dataset}-${row.csvRow}`,
        dataset: row.dataset,
        csvRow: row.csvRow,
        status: (row.reason === "ambiguous" ? "Ambiguous" : "Unmatched") as
          | "Ambiguous"
          | "Unmatched",
        rule: "",
        group: "",
        record: row.record,
        remarks:
          row.reason === "ambiguous"
            ? "Ambiguous candidate matches found"
            : "No matching candidate found",
        score: undefined,
      }))
    )
    return items
  }, [run])

  const datasetOptions = useMemo(() => {
    if (!run) return []
    return Object.keys(run.datasets)
  }, [run])

  const filteredRows = useMemo(() => {
    const term = searchTerm.trim().toLowerCase()
    return allRows.filter((row) => {
      if (statusFilter !== "All" && row.status !== statusFilter) return false
      if (datasetFilter !== "All" && row.dataset !== datasetFilter) return false
      if (!term) return true

      if (row.group.toLowerCase().includes(term)) return true
      if (row.rule.toLowerCase().includes(term)) return true
      if (row.dataset.toLowerCase().includes(term)) return true
      if (String(row.csvRow).includes(term)) return true
      if (row.remarks && row.remarks.toLowerCase().includes(term)) return true

      for (const [key, value] of Object.entries(row.record)) {
        if (
          key.toLowerCase().includes(term) ||
          String(value).toLowerCase().includes(term)
        ) {
          return true
        }
      }
      return false
    })
  }, [allRows, searchTerm, statusFilter, datasetFilter])

  const totalPages = Math.max(1, Math.ceil(filteredRows.length / pageSize))
  const safePage = Math.min(page, totalPages)

  const paginatedRows = useMemo(() => {
    const start = (safePage - 1) * pageSize
    return filteredRows.slice(start, start + pageSize)
  }, [filteredRows, safePage, pageSize])

  const selected = useMemo(
    () => allRows.find((row) => row.id === selectedId),
    [allRows, selectedId]
  )

  function updateFilter<T>(setter: (val: T) => void, val: T) {
    setter(val)
    setPage(1)
  }

  function handleJumpPage(e: React.FormEvent) {
    e.preventDefault()
    const target = Number.parseInt(jumpPageInput, 10)
    if (!Number.isNaN(target) && target >= 1 && target <= totalPages) {
      setPage(target)
      setJumpPageInput("")
    }
  }

  if (result.isLoading)
    return <p className="text-sm text-muted-foreground">Loading run records…</p>
  if (result.error || !run)
    return (
      <ErrorNote
        message={
          result.error instanceof Error
            ? result.error.message
            : "Run not found."
        }
      />
    )

  const startRecord = filteredRows.length === 0 ? 0 : (safePage - 1) * pageSize + 1
  const endRecord = Math.min(safePage * pageSize, filteredRows.length)

  return (
    <section className="flex flex-col gap-5">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="text-2xl font-medium">{run.profileName}</h1>
          <p className="mt-1 text-sm text-muted-foreground">
            Version {run.profileVersion} · {run.status} ·{" "}
            {new Date(run.creationTime).toLocaleString()}
          </p>
        </div>
        <div className="flex items-center gap-2">
          <Button
            variant="outline"
            render={<Link to={`/reconciliation/runs/${run.id}`} />}
          >
            Run summary
          </Button>
          <Button
            variant="outline"
            size="sm"
            disabled={!filteredRows.length}
            onClick={() =>
              downloadCsv(
                `${run.id}-records.csv`,
                [
                  "Group",
                  "Status",
                  "Dataset",
                  "Row",
                  "System Remarks",
                  "Match Similarity",
                  "Rule",
                  "Record",
                  "Evidence",
                ],
                filteredRows.map((row) => [
                  row.group ? `#${row.group}` : "—",
                  row.status,
                  row.dataset,
                  String(row.csvRow),
                  row.remarks ?? "",
                  row.score != null
                    ? `${(row.score * 100).toFixed(2)}%`
                    : row.status === "Matched"
                    ? "100%"
                    : "—",
                  row.rule,
                  JSON.stringify(row.record),
                  row.evidence ? JSON.stringify(row.evidence) : "",
                ])
              )
            }
          >
            <Download className="size-4" />
            Export ({filteredRows.length.toLocaleString()})
          </Button>
        </div>
      </div>

      {/* Filter and Search Bar */}
      <div className="grid gap-3 rounded-lg border bg-card p-4 sm:grid-cols-2 lg:grid-cols-4">
        <div className="relative col-span-1 sm:col-span-2">
          <Search className="absolute left-3 top-2.5 size-4 text-muted-foreground" />
          <input
            className={`${inputClass} pl-9 pr-8`}
            placeholder="Search records, keys, groups, rules..."
            value={searchTerm}
            onChange={(e) => updateFilter(setSearchTerm, e.target.value)}
          />
          {searchTerm ? (
            <button
              type="button"
              className="absolute right-2.5 top-2.5 text-muted-foreground hover:text-foreground"
              onClick={() => updateFilter(setSearchTerm, "")}
              aria-label="Clear search"
            >
              <X className="size-4" />
            </button>
          ) : null}
        </div>

        <div className="flex items-center gap-2">
          <Filter className="size-4 shrink-0 text-muted-foreground" />
          <select
            className={inputClass}
            value={statusFilter}
            onChange={(e) => updateFilter(setStatusFilter, e.target.value)}
            aria-label="Filter by status"
          >
            <option value="All">All statuses</option>
            <option value="Matched">Matched</option>
            <option value="Partial Match">Partial Match</option>
            <option value="Unmatched">Unmatched</option>
            <option value="Ambiguous">Ambiguous</option>
          </select>
        </div>

        <div>
          <select
            className={inputClass}
            value={datasetFilter}
            onChange={(e) => updateFilter(setDatasetFilter, e.target.value)}
            aria-label="Filter by dataset"
          >
            <option value="All">All datasets</option>
            {datasetOptions.map((code) => (
              <option key={code} value={code}>
                {code}
              </option>
            ))}
          </select>
        </div>
      </div>

      {/* Main Grid Card */}
      <div className="overflow-hidden border bg-card">
        <div className="flex flex-wrap items-center justify-between gap-3 border-b px-4 py-3">
          <div className="flex items-center gap-2 text-sm">
            <span className="font-medium text-foreground">
              Showing {startRecord.toLocaleString()}–{endRecord.toLocaleString()}
            </span>
            <span className="text-muted-foreground">
              of {filteredRows.length.toLocaleString()} records
              {filteredRows.length !== allRows.length ? (
                <> (filtered from {allRows.length.toLocaleString()} total)</>
              ) : null}
            </span>
            <span className="text-muted-foreground">·</span>
            <span className="text-xs text-muted-foreground">
              {run.matchedGroups.toLocaleString()} matched groups
            </span>
          </div>

          <div className="flex items-center gap-2">
            <span className="text-xs text-muted-foreground">Per page:</span>
            <select
              className="h-8 rounded border bg-background px-2 text-xs"
              value={pageSize}
              onChange={(e) => {
                setPageSize(Number(e.target.value))
                setPage(1)
              }}
              aria-label="Page size"
            >
              <option value={25}>25</option>
              <option value={50}>50</option>
              <option value={100}>100</option>
              <option value={250}>250</option>
            </select>
          </div>
        </div>

        {/* Windowed Table View */}
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead>
              <tr className="border-b bg-muted/40 text-left text-xs text-muted-foreground">
                <th className="w-20 px-4 py-2.5">Group</th>
                <th className="w-32 px-4 py-2.5">Status</th>
                <th className="w-28 px-4 py-2.5">Dataset</th>
                <th className="w-20 px-4 py-2.5">Row</th>
                <th className="min-w-[260px] px-4 py-2.5">System Remarks</th>
                <th className="w-36 px-4 py-2.5" title="Similarity of compared fields, not a probability that the whole transaction is correct.">Match Similarity</th>
                <th className="w-24 px-4 py-2.5 text-right">Details</th>
              </tr>
            </thead>
            <tbody>
              {paginatedRows.length === 0 ? (
                <tr>
                  <td
                    colSpan={7}
                    className="px-4 py-12 text-center text-muted-foreground"
                  >
                    {allRows.length === 0
                      ? "This run contains no records."
                      : "No records match the current filters."}
                  </td>
                </tr>
              ) : null}
              {paginatedRows.map((row) => {
                const isSelected = selectedId === row.id
                return (
                  <tr
                    key={row.id}
                    onClick={() => setSelectedId(row.id)}
                    className={`cursor-pointer border-b transition-colors last:border-0 ${
                      isSelected ? "bg-muted/60" : "hover:bg-muted/30"
                    }`}
                  >
                    <td className="px-4 py-2.5 font-mono text-xs font-semibold text-foreground">
                      {row.group ? `#${row.group}` : <span className="text-muted-foreground">—</span>}
                    </td>
                    <td className="px-4 py-2.5">
                      <span
                        className={`inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-medium ${
                          row.status === "Matched"
                            ? "bg-emerald-500/10 text-emerald-600 dark:text-emerald-400 border border-emerald-500/20"
                            : row.status === "Partial Match"
                            ? "bg-amber-500/10 text-amber-600 dark:text-amber-400 border border-amber-500/20 font-semibold"
                            : row.status === "Ambiguous"
                            ? "bg-rose-500/10 text-rose-600 dark:text-rose-400 border border-rose-500/20"
                            : "bg-slate-500/10 text-slate-600 dark:text-slate-400 border border-slate-500/20"
                        }`}
                      >
                        {row.status}
                      </span>
                    </td>
                    <td className="px-4 py-2.5 font-mono text-xs font-semibold text-foreground">
                      {row.dataset}
                    </td>
                    <td className="px-4 py-2.5 font-mono text-xs tabular-nums text-muted-foreground">
                      #{row.csvRow}
                    </td>
                    <td className="px-4 py-2.5 text-xs text-foreground">
                      {row.status === "Partial Match" ? (
                        <div className="flex items-center gap-1.5 font-medium text-amber-700 dark:text-amber-300">
                          <span className="size-1.5 rounded-full bg-amber-500 shrink-0" />
                          <span className="line-clamp-2" title={row.remarks}>
                            {row.remarks || "Partial match on subset of keys"}
                          </span>
                        </div>
                      ) : row.status === "Matched" ? (
                        <div className="flex items-center gap-1.5 text-muted-foreground">
                          <span className="size-1.5 rounded-full bg-emerald-500 shrink-0" />
                          <span className="line-clamp-1" title={row.remarks || "Exact match on all keys"}>
                            {row.remarks || "Exact match on all keys"}
                          </span>
                        </div>
                      ) : row.status === "Ambiguous" ? (
                        <div className="flex items-center gap-1.5 text-rose-600 dark:text-rose-400">
                          <span className="size-1.5 rounded-full bg-rose-500 shrink-0" />
                          <span className="line-clamp-1" title={row.remarks || "Ambiguous candidate records"}>
                            {row.remarks || "Ambiguous candidate records"}
                          </span>
                        </div>
                      ) : (
                        <span className="text-muted-foreground line-clamp-1" title={row.remarks || "No match candidate found"}>
                          {row.remarks || "No match candidate found"}
                        </span>
                      )}
                    </td>
                    <td className="px-4 py-2.5 font-mono text-xs">
                      {row.score != null ? (
                        <span
                          className={`inline-flex items-center gap-1 rounded px-2 py-0.5 font-semibold text-xs ${
                            row.score >= 0.9
                              ? "bg-emerald-500/10 text-emerald-700 dark:text-emerald-400 border border-emerald-500/20"
                              : row.score >= 0.7
                              ? "bg-amber-500/10 text-amber-700 dark:text-amber-400 border border-amber-500/20"
                              : "bg-rose-500/10 text-rose-700 dark:text-rose-400 border border-rose-500/20"
                          }`}
                        >
                          {(row.score * 100).toFixed(2)}%
                        </span>
                      ) : row.status === "Matched" ? (
                        <span className="inline-flex items-center gap-1 rounded bg-emerald-500/10 px-2 py-0.5 font-semibold text-xs text-emerald-700 dark:text-emerald-400 border border-emerald-500/20">
                          100%
                        </span>
                      ) : (
                        <span className="text-muted-foreground">—</span>
                      )}
                    </td>
                    <td className="px-4 py-2.5 text-right" onClick={(e) => e.stopPropagation()}>
                      <Button
                        variant={isSelected ? "secondary" : "outline"}
                        size="sm"
                        className="h-7 text-xs flex items-center gap-1.5 ml-auto"
                        aria-label={`View ${row.dataset} row ${row.csvRow} details`}
                        aria-pressed={isSelected}
                        onClick={() =>
                          setSelectedId(isSelected ? null : row.id)
                        }
                      >
                        <Eye className="size-3.5" />
                        Details
                      </Button>
                    </td>
                  </tr>
                )
              })}
            </tbody>
          </table>
        </div>

        {/* Pagination Controls */}
        <div className="flex flex-wrap items-center justify-between gap-3 border-t px-4 py-3 text-xs">
          <div className="flex items-center gap-1.5">
            <Button
              variant="outline"
              size="icon-sm"
              disabled={safePage <= 1}
              onClick={() => setPage(1)}
              aria-label="First page"
            >
              <ChevronsLeft className="size-3.5" />
            </Button>
            <Button
              variant="outline"
              size="icon-sm"
              disabled={safePage <= 1}
              onClick={() => setPage((p) => Math.max(1, p - 1))}
              aria-label="Previous page"
            >
              <ChevronLeft className="size-3.5" />
            </Button>

            <span className="px-2 text-muted-foreground">
              Page <strong className="text-foreground">{safePage}</strong> of{" "}
              <strong className="text-foreground">{totalPages}</strong>
            </span>

            <Button
              variant="outline"
              size="icon-sm"
              disabled={safePage >= totalPages}
              onClick={() => setPage((p) => Math.min(totalPages, p + 1))}
              aria-label="Next page"
            >
              <ChevronRight className="size-3.5" />
            </Button>
            <Button
              variant="outline"
              size="icon-sm"
              disabled={safePage >= totalPages}
              onClick={() => setPage(totalPages)}
              aria-label="Last page"
            >
              <ChevronsRight className="size-3.5" />
            </Button>
          </div>

          <form onSubmit={handleJumpPage} className="flex items-center gap-2">
            <span className="text-muted-foreground">Go to page:</span>
            <input
              type="number"
              min={1}
              max={totalPages}
              value={jumpPageInput}
              onChange={(e) => setJumpPageInput(e.target.value)}
              className="h-8 w-14 rounded border bg-background px-2 text-center text-xs"
              placeholder={String(safePage)}
              aria-label="Jump to page number"
            />
            <Button type="submit" variant="outline" size="sm" className="h-8 px-2 text-xs">
              Go
            </Button>
          </form>
        </div>
      </div>

      {/* Selected Row Detail Modal Drilldown */}
      <Modal
        isOpen={Boolean(selected)}
        onClose={() => setSelectedId(null)}
        title={selected ? `${selected.dataset} · Row #${selected.csvRow} Record Drilldown` : ""}
        description={
          selected ? (
            <span className="flex flex-wrap items-center gap-x-2 gap-y-1">
              <span>Status: <strong className="text-foreground">{selected.status}</strong></span>
              {selected.group ? <span>· Group #{selected.group}</span> : null}
              {selected.rule ? <span>· Rule: {selected.rule}</span> : null}
            </span>
          ) : undefined
        }
        size="4xl"
        footer={
          <Button variant="outline" size="sm" onClick={() => setSelectedId(null)}>
            Close
          </Button>
        }
      >
        {selected ? (
          <div className="flex flex-col gap-5">
            <div>
              <h3 className="text-xs font-semibold uppercase tracking-wider text-muted-foreground mb-2.5">
                Record Attributes
              </h3>
              <dl className="grid gap-3 sm:grid-cols-2 md:grid-cols-3 lg:grid-cols-4">
                {Object.entries(selected.record).map(([key, value]) => (
                  <div key={key} className="rounded border bg-muted/20 p-2.5">
                    <dt className="text-xs font-medium text-muted-foreground">{key}</dt>
                    <dd className="mt-1 font-mono text-xs break-words text-foreground">
                      {value || "—"}
                    </dd>
                  </div>
                ))}
              </dl>
            </div>

            {selected.evidence ? (
              <div className="flex flex-col gap-3 rounded border border-primary/20 bg-primary/5 p-4 text-xs text-muted-foreground">
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <div>
                    <strong className="text-foreground text-sm">Reconciliation Evidence: </strong>
                    <span className="font-mono text-xs font-semibold px-2 py-0.5 rounded bg-muted/40 text-foreground">
                      {selected.evidence.comparison}
                    </span>
                  </div>
                  {selected.score != null ? (
                    <span className="rounded bg-primary/10 px-2.5 py-1 font-mono text-xs font-bold text-primary">
                      Match Similarity: {(selected.score * 100).toFixed(2)}%
                    </span>
                  ) : null}
                </div>

                {selected.remarks ? (
                  <div className="rounded border bg-background/90 p-3 text-foreground shadow-xs">
                    <span className="font-semibold text-amber-600 dark:text-amber-400">
                      Recon Remarks:{" "}
                    </span>
                    <span>{selected.remarks}</span>
                  </div>
                ) : selected.evidence.comparison === "sum" ? (
                  <p className="text-foreground">
                    Source sum {selected.evidence.sourceSum}; target {selected.evidence.target}; variance {selected.evidence.variance}.
                  </p>
                ) : (
                  <p className="text-foreground">All configured normalized keys are equal.</p>
                )}
              </div>
            ) : null}
          </div>
        ) : null}
      </Modal>
    </section>
  )
}
