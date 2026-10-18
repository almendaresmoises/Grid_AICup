import { Button } from "@workspace/ui/components/button"
import { useQuery } from "@tanstack/react-query"
import {
  AlertCircle,
  AlertTriangle,
  ArrowLeft,
  Check,
  CheckCircle2,
  ChevronLeft,
  ChevronRight,
  ChevronsLeft,
  ChevronsRight,
  Download,
  Eye,
  Filter,
  GitCompare,
  Search,
  X,
} from "lucide-react"
import { useMemo, useState } from "react"
import { Link, useParams } from "react-router"
import { cn } from "cn"
import { ErrorNote } from "@/components/error-note"
import { Modal } from "@/components/modal"
import { api } from "@/lib/api"
import { downloadCsv } from "@/lib/csv"
import { parseProfile, type Evidence, type Profile, type RunDetail } from "@/lib/types"
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
  const [counterpartIndex, setCounterpartIndex] = useState(0)
  const [searchTerm, setSearchTerm] = useState("")
  const [statusFilter, setStatusFilter] = useState<string>("All")
  const [datasetFilter, setDatasetFilter] = useState<string>("All")
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(50)
  const [jumpPageInput, setJumpPageInput] = useState("")

  const run = result.data

  const profile = useQuery({
    queryKey: ["profile", run?.profileId],
    queryFn: () => api<Profile>(`/api/app/profile/${run?.profileId}`),
    enabled: Boolean(run?.profileId),
  })

  const profileSpec = useMemo(() => {
    if (!profile.data) return null
    try {
      const raw =
        profile.data.approvedDefinitionJson || profile.data.definitionJson
      return raw ? parseProfile(raw) : null
    } catch {
      return null
    }
  }, [profile.data])

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

  const groupIndex = selected?.group
    ? Number.parseInt(selected.group, 10) - 1
    : -1
  const matchGroup =
    groupIndex >= 0 && run ? run.groups[groupIndex] : null

  const currentRule = useMemo(() => {
    if (!matchGroup || !profileSpec) return null
    return profileSpec.rules.find((r) => r.id === matchGroup.rule) ?? null
  }, [matchGroup, profileSpec])

  const counterpartRecords = useMemo(() => {
    if (!matchGroup || !run || !selected) return []
    const counterparts: {
      dataset: string
      csvRow: number
      record: Record<string, string>
    }[] = []

    for (const [dsCode, rowNumbers] of Object.entries(matchGroup.records)) {
      for (const rowNum of rowNumbers) {
        if (dsCode === selected.dataset && rowNum === selected.csvRow) continue
        const data = run.datasets[dsCode]?.[rowNum - 2]
        if (data) {
          counterparts.push({
            dataset: dsCode,
            csvRow: rowNum,
            record: data,
          })
        }
      }
    }
    return counterparts
  }, [matchGroup, run, selected])

  const safeCounterpartIndex = Math.min(
    counterpartIndex,
    Math.max(0, counterpartRecords.length - 1)
  )
  const activeCounterpart = counterpartRecords[safeCounterpartIndex] || null

  const comparisonData = useMemo(() => {
    const diffLeft = new Set<string>()
    const diffRight = new Set<string>()
    const rows: {
      keyLabel: string
      keyType?: string
      leftField: string
      leftValue: string
      rightField: string
      rightValue: string
      isDifference: boolean
      reason?: string
    }[] = []

    if (!activeCounterpart || !selected) {
      return { diffLeft, diffRight, rows }
    }

    const divergentKeyName = matchGroup?.evidence?.divergent_key?.toLowerCase()

    if (currentRule && currentRule.keys.length > 0) {
      for (const k of currentRule.keys) {
        const leftField = k.fields[selected.dataset] || ""
        const rightField = k.fields[activeCounterpart.dataset] || ""
        const leftVal = leftField ? (selected.record[leftField] ?? "") : ""
        const rightVal = rightField
          ? (activeCounterpart.record[rightField] ?? "")
          : ""

        const isMarkedDivergent =
          Boolean(divergentKeyName) &&
          (leftField.toLowerCase() === divergentKeyName ||
            rightField.toLowerCase() === divergentKeyName ||
            k.type.toLowerCase() === divergentKeyName)

        const isPartialMode =
          k.match_mode === "partial" ||
          k.match_mode === "fuzzy" ||
          k.match_mode === "divergent" ||
          k.expected_mismatch === true

        const valuesDiffer =
          leftVal.trim().toLowerCase() !== rightVal.trim().toLowerCase()

        const isDiff = isMarkedDivergent || isPartialMode || valuesDiffer

        if (isDiff) {
          if (leftField) diffLeft.add(leftField)
          if (rightField) diffRight.add(rightField)
        }

        rows.push({
          keyLabel:
            leftField && rightField && leftField !== rightField
              ? `${leftField} ↔ ${rightField}`
              : leftField || rightField || k.type,
          keyType: k.type,
          leftField,
          leftValue: leftVal,
          rightField,
          rightValue: rightVal,
          isDifference: isDiff,
          reason: isMarkedDivergent
            ? `Divergent key: ${matchGroup?.evidence?.divergent_key}`
            : isPartialMode
            ? `Allowed by rule (${k.match_mode || "partial"})`
            : valuesDiffer
            ? "Values differ"
            : undefined,
        })
      }
    } else {
      const allKeys = Array.from(
        new Set([
          ...Object.keys(selected.record),
          ...Object.keys(activeCounterpart.record),
        ])
      )
      for (const key of allKeys) {
        const leftVal = selected.record[key] ?? ""
        const rightVal = activeCounterpart.record[key] ?? ""
        const valuesDiffer =
          leftVal.trim().toLowerCase() !== rightVal.trim().toLowerCase()
        const isMarkedDivergent =
          Boolean(divergentKeyName) && key.toLowerCase() === divergentKeyName

        const isDiff = valuesDiffer || isMarkedDivergent
        if (isDiff) {
          diffLeft.add(key)
          diffRight.add(key)
        }
        rows.push({
          keyLabel: key,
          leftField: key,
          leftValue: leftVal,
          rightField: key,
          rightValue: rightVal,
          isDifference: isDiff,
          reason: isDiff ? "Values differ" : undefined,
        })
      }
    }

    if (divergentKeyName) {
      for (const k of Object.keys(selected.record)) {
        if (k.toLowerCase() === divergentKeyName) diffLeft.add(k)
      }
      for (const k of Object.keys(activeCounterpart.record)) {
        if (k.toLowerCase() === divergentKeyName) diffRight.add(k)
      }
    }

    return { diffLeft, diffRight, rows }
  }, [activeCounterpart, selected, currentRule, matchGroup])

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
      <div>
        <Link
          to="/reconciliation/runs"
          className="mb-2 inline-flex items-center gap-1.5 text-xs text-muted-foreground transition-colors hover:text-foreground"
        >
          <ArrowLeft className="size-3.5" />
          <span>Back to reconciliation runs</span>
        </Link>
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
              render={<Link to="/reconciliation/runs" />}
            >
              <ArrowLeft className="size-4" />
              Back to runs
            </Button>
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
        onClose={() => {
          setSelectedId(null)
          setCounterpartIndex(0)
        }}
        title={
          selected ? (
            <div className="flex flex-wrap items-center gap-2">
              <span>
                {selected.dataset} · Row #{selected.csvRow} Record Drilldown
              </span>
            </div>
          ) : (
            ""
          )
        }
        description={
          selected ? (
            <div className="flex flex-wrap items-center gap-2 pt-0.5">
              <span
                className={cn(
                  "rounded-full px-2.5 py-0.5 font-medium text-xs border",
                  selected.status === "Matched"
                    ? "bg-emerald-500/10 text-emerald-600 dark:text-emerald-400 border-emerald-500/20"
                    : selected.status === "Partial Match"
                    ? "bg-amber-500/10 text-amber-600 dark:text-amber-400 border-amber-500/20 font-semibold"
                    : selected.status === "Ambiguous"
                    ? "bg-purple-500/10 text-purple-600 dark:text-purple-400 border-purple-500/20"
                    : "bg-red-500/10 text-red-600 dark:text-red-400 border-red-500/20"
                )}
              >
                {selected.status}
                {selected.score != null
                  ? ` (${(selected.score * 100).toFixed(1)}%)`
                  : ""}
              </span>
              {selected.group ? (
                <span className="rounded bg-muted px-2 py-0.5 font-mono text-xs text-muted-foreground">
                  Group #{selected.group}
                </span>
              ) : null}
              {selected.rule ? (
                <span className="rounded bg-muted px-2 py-0.5 font-mono text-xs text-muted-foreground">
                  Rule: {selected.rule}
                </span>
              ) : null}
            </div>
          ) : undefined
        }
        size="5xl"
        footer={
          <div className="flex w-full items-center justify-between">
            <div className="text-xs text-muted-foreground">
              {activeCounterpart ? (
                <span>
                  Matched with{" "}
                  <strong className="text-foreground">
                    {activeCounterpart.dataset} (Row #{activeCounterpart.csvRow})
                  </strong>
                </span>
              ) : (
                <span>Unresolved record</span>
              )}
            </div>
            <Button
              variant="outline"
              size="sm"
              onClick={() => {
                setSelectedId(null)
                setCounterpartIndex(0)
              }}
            >
              Close
            </Button>
          </div>
        }
      >
        {selected ? (
          <div className="flex flex-col gap-6">
            {/* Reconciliation Match / Evidence Summary Banner */}
            {selected.evidence ? (
              <div
                className={cn(
                  "flex flex-col gap-3 rounded-xl border p-4 text-xs",
                  selected.status === "Partial Match"
                    ? "border-amber-500/30 bg-amber-500/10 text-amber-900 dark:text-amber-200"
                    : "border-primary/20 bg-primary/5 text-muted-foreground"
                )}
              >
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <div className="flex items-center gap-2">
                    {selected.status === "Partial Match" ? (
                      <AlertTriangle className="size-4 shrink-0 text-amber-600 dark:text-amber-400" />
                    ) : (
                      <CheckCircle2 className="size-4 shrink-0 text-emerald-600 dark:text-emerald-400" />
                    )}
                    <span className="font-semibold text-foreground text-sm">
                      {selected.status === "Partial Match"
                        ? "Partial Match Reconciliation Evidence"
                        : "Match Reconciliation Evidence"}
                    </span>
                    <span className="rounded bg-background/80 px-2 py-0.5 font-mono text-xs font-semibold text-foreground border">
                      {selected.evidence.comparison}
                    </span>
                  </div>
                  {selected.score != null ? (
                    <span
                      className={cn(
                        "rounded px-2.5 py-0.5 font-mono text-xs font-bold border",
                        selected.status === "Partial Match"
                          ? "bg-amber-500/20 text-amber-800 dark:text-amber-200 border-amber-500/30"
                          : "bg-primary/10 text-primary border-primary/20"
                      )}
                    >
                      Match Similarity: {(selected.score * 100).toFixed(2)}%
                    </span>
                  ) : null}
                </div>

                {selected.remarks ? (
                  <div className="rounded-lg border bg-background/90 p-3 text-xs text-foreground shadow-xs">
                    <span className="font-semibold text-amber-600 dark:text-amber-400">
                      Reconciliation Remarks:{" "}
                    </span>
                    <span>{selected.remarks}</span>
                  </div>
                ) : selected.evidence.comparison === "sum" ? (
                  <p className="text-foreground">
                    Source sum {selected.evidence.sourceSum}; target{" "}
                    {selected.evidence.target}; variance{" "}
                    {selected.evidence.variance}.
                  </p>
                ) : (
                  <p className="text-foreground">
                    All configured normalized keys are equal.
                  </p>
                )}

                {/* Multiple counterpart switcher if 1:N or aggregate match */}
                {counterpartRecords.length > 1 && (
                  <div className="flex items-center gap-2 pt-1 border-t border-border/40">
                    <span className="text-xs font-medium text-foreground">
                      Matched records ({counterpartRecords.length}):
                    </span>
                    <div className="flex flex-wrap gap-1">
                      {counterpartRecords.map((cp, idx) => (
                        <button
                          key={`${cp.dataset}-${cp.csvRow}`}
                          type="button"
                          onClick={() => setCounterpartIndex(idx)}
                          className={cn(
                            "rounded px-2 py-1 text-xs font-medium transition-colors",
                            safeCounterpartIndex === idx
                              ? "bg-primary text-primary-foreground shadow-xs"
                              : "bg-muted text-muted-foreground hover:text-foreground"
                          )}
                        >
                          {cp.dataset} (Row #{cp.csvRow})
                        </button>
                      ))}
                    </div>
                  </div>
                )}
              </div>
            ) : null}

            {/* SIDE-BY-SIDE COMPARISON (When a match exists) */}
            {activeCounterpart ? (
              <div className="flex flex-col gap-6">
                {/* 1. Key-by-Key Comparison Table */}
                {comparisonData.rows.length > 0 && (
                  <div className="overflow-hidden rounded-xl border bg-card shadow-xs">
                    <div className="flex flex-wrap items-center justify-between gap-2 border-b bg-muted/40 px-4 py-2.5">
                      <div className="flex items-center gap-2">
                        <GitCompare className="size-4 text-primary" />
                        <h4 className="text-xs font-semibold uppercase tracking-wider text-foreground">
                          Key Alignment & Differences
                        </h4>
                      </div>
                      <div className="flex items-center gap-2 text-[11px] text-muted-foreground">
                        <span className="inline-flex items-center gap-1 rounded-full bg-emerald-500/10 border border-emerald-500/20 px-2 py-0.5 text-emerald-600 dark:text-emerald-400 font-medium">
                          <Check className="size-3" /> Identical
                        </span>
                        <span className="inline-flex items-center gap-1 rounded-full bg-amber-500/10 border border-amber-500/20 px-2 py-0.5 text-amber-600 dark:text-amber-400 font-medium">
                          <AlertCircle className="size-3" /> Difference
                        </span>
                      </div>
                    </div>

                    <div className="overflow-x-auto">
                      <table className="w-full text-left text-xs">
                        <thead className="border-b bg-muted/20">
                          <tr>
                            <th className="px-3.5 py-2 font-medium text-muted-foreground">
                              Reconciliation Key
                            </th>
                            <th className="px-3.5 py-2 font-medium text-muted-foreground">
                              {selected.dataset} (Row #{selected.csvRow})
                            </th>
                            <th className="px-3.5 py-2 font-medium text-muted-foreground">
                              {activeCounterpart.dataset} (Row #{activeCounterpart.csvRow})
                            </th>
                            <th className="px-3.5 py-2 text-right font-medium text-muted-foreground">
                              Match Status
                            </th>
                          </tr>
                        </thead>
                        <tbody className="divide-y divide-border">
                          {comparisonData.rows.map((row, idx) => (
                            <tr
                              key={idx}
                              className={cn(
                                "transition-colors",
                                row.isDifference
                                  ? "bg-amber-500/10 dark:bg-amber-500/15"
                                  : "hover:bg-muted/30"
                              )}
                            >
                              <td className="px-3.5 py-2.5 font-mono">
                                <div className="flex flex-col">
                                  <span className="font-semibold text-foreground">
                                    {row.keyLabel}
                                  </span>
                                  {row.keyType && (
                                    <span className="text-[10px] text-muted-foreground">
                                      type: {row.keyType}
                                    </span>
                                  )}
                                </div>
                              </td>
                              <td className="px-3.5 py-2.5 font-mono">
                                <span
                                  className={cn(
                                    row.isDifference
                                      ? "rounded bg-amber-500/20 px-1.5 py-0.5 font-bold text-amber-900 dark:text-amber-200"
                                      : "text-foreground"
                                  )}
                                >
                                  {row.leftValue || "—"}
                                </span>
                              </td>
                              <td className="px-3.5 py-2.5 font-mono">
                                <span
                                  className={cn(
                                    row.isDifference
                                      ? "rounded bg-amber-500/20 px-1.5 py-0.5 font-bold text-amber-900 dark:text-amber-200"
                                      : "text-foreground"
                                  )}
                                >
                                  {row.rightValue || "—"}
                                </span>
                              </td>
                              <td className="px-3.5 py-2.5 text-right">
                                {row.isDifference ? (
                                  <span className="inline-flex items-center gap-1 rounded-full border border-amber-500/30 bg-amber-500/20 px-2 py-0.5 text-[11px] font-semibold text-amber-800 dark:text-amber-200">
                                    <AlertCircle className="size-3" />
                                    {row.reason || "Difference"}
                                  </span>
                                ) : (
                                  <span className="inline-flex items-center gap-1 rounded-full border border-emerald-500/30 bg-emerald-500/15 px-2 py-0.5 text-[11px] font-semibold text-emerald-700 dark:text-emerald-300">
                                    <Check className="size-3" />
                                    Identical
                                  </span>
                                )}
                              </td>
                            </tr>
                          ))}
                        </tbody>
                      </table>
                    </div>
                  </div>
                )}

                {/* 2. Side-by-Side Full Record Attribute Panels */}
                <div>
                  <h4 className="mb-2.5 text-xs font-semibold uppercase tracking-wider text-muted-foreground">
                    Side-by-Side Record Attributes
                  </h4>
                  <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
                    {/* Left: Selected Record */}
                    <div className="rounded-xl border bg-card p-4 shadow-xs">
                      <div className="mb-3 flex items-center justify-between border-b pb-2.5">
                        <div className="flex items-center gap-2">
                          <span className="rounded bg-muted px-2 py-0.5 font-mono text-xs font-semibold text-foreground">
                            {selected.dataset}
                          </span>
                          <span className="text-xs font-semibold text-foreground">
                            Row #{selected.csvRow}
                          </span>
                        </div>
                        <span className="text-[11px] font-medium text-muted-foreground">
                          Inspected Record
                        </span>
                      </div>
                      <dl className="grid grid-cols-1 gap-2.5 sm:grid-cols-2">
                        {Object.entries(selected.record).map(([k, v]) => {
                          const isDiff = comparisonData.diffLeft.has(k)
                          return (
                            <div
                              key={k}
                              className={cn(
                                "rounded-lg border p-2.5 transition-colors",
                                isDiff
                                  ? "border-amber-500/50 bg-amber-500/10 dark:bg-amber-500/15 ring-1 ring-amber-500/30"
                                  : "border-border bg-muted/20"
                              )}
                            >
                              <div className="flex items-center justify-between">
                                <dt className="text-[11px] font-medium text-muted-foreground">
                                  {k}
                                </dt>
                                {isDiff && (
                                  <span className="rounded bg-amber-500/20 px-1 py-0.2 text-[10px] font-semibold text-amber-700 dark:text-amber-400">
                                    Diff
                                  </span>
                                )}
                              </div>
                              <dd
                                className={cn(
                                  "mt-0.5 break-words font-mono text-xs",
                                  isDiff
                                    ? "font-bold text-amber-900 dark:text-amber-200"
                                    : "text-foreground"
                                )}
                              >
                                {v || "—"}
                              </dd>
                            </div>
                          )
                        })}
                      </dl>
                    </div>

                    {/* Right: Matched Counterpart Record */}
                    <div className="rounded-xl border bg-card p-4 shadow-xs">
                      <div className="mb-3 flex items-center justify-between border-b pb-2.5">
                        <div className="flex items-center gap-2">
                          <span className="rounded bg-muted px-2 py-0.5 font-mono text-xs font-semibold text-foreground">
                            {activeCounterpart.dataset}
                          </span>
                          <span className="text-xs font-semibold text-foreground">
                            Row #{activeCounterpart.csvRow}
                          </span>
                        </div>
                        <span className="text-[11px] font-semibold text-emerald-600 dark:text-emerald-400">
                          Matched Counterpart
                        </span>
                      </div>
                      <dl className="grid grid-cols-1 gap-2.5 sm:grid-cols-2">
                        {Object.entries(activeCounterpart.record).map(([k, v]) => {
                          const isDiff = comparisonData.diffRight.has(k)
                          return (
                            <div
                              key={k}
                              className={cn(
                                "rounded-lg border p-2.5 transition-colors",
                                isDiff
                                  ? "border-amber-500/50 bg-amber-500/10 dark:bg-amber-500/15 ring-1 ring-amber-500/30"
                                  : "border-border bg-muted/20"
                              )}
                            >
                              <div className="flex items-center justify-between">
                                <dt className="text-[11px] font-medium text-muted-foreground">
                                  {k}
                                </dt>
                                {isDiff && (
                                  <span className="rounded bg-amber-500/20 px-1 py-0.2 text-[10px] font-semibold text-amber-700 dark:text-amber-400">
                                    Diff
                                  </span>
                                )}
                              </div>
                              <dd
                                className={cn(
                                  "mt-0.5 break-words font-mono text-xs",
                                  isDiff
                                    ? "font-bold text-amber-900 dark:text-amber-200"
                                    : "text-foreground"
                                )}
                              >
                                {v || "—"}
                              </dd>
                            </div>
                          )
                        })}
                      </dl>
                    </div>
                  </div>
                </div>
              </div>
            ) : (
              /* Single Record View when Unmatched or Ambiguous */
              <div>
                <h3 className="mb-2.5 text-xs font-semibold uppercase tracking-wider text-muted-foreground">
                  Record Attributes
                </h3>
                <dl className="grid gap-3 sm:grid-cols-2 md:grid-cols-3 lg:grid-cols-4">
                  {Object.entries(selected.record).map(([key, value]) => (
                    <div key={key} className="rounded-lg border bg-muted/20 p-2.5">
                      <dt className="text-xs font-medium text-muted-foreground">
                        {key}
                      </dt>
                      <dd className="mt-1 break-words font-mono text-xs text-foreground">
                        {value || "—"}
                      </dd>
                    </div>
                  ))}
                </dl>
              </div>
            )}
          </div>
        ) : null}
      </Modal>
    </section>
  )
}
