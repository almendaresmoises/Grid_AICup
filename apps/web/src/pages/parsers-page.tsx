import { Button } from "@workspace/ui/components/button"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { Database, Plus, Search, Settings2, Trash2 } from "lucide-react"
import { useMemo, useState } from "react"
import { Link } from "react-router"
import { ErrorNote } from "@/components/error-note"
import { OriginCell } from "@/components/origin-cell"
import { api } from "@/lib/api"
import { type CanonicalDataset, type ParserConfiguration } from "@/lib/canonical"
import { readStudio } from "@/lib/parser-studio"

export function ParsersPage() {
  const queryClient = useQueryClient()
  const parsers = useQuery({
    queryKey: ["parsers"],
    queryFn: () => api<ParserConfiguration[]>("/api/app/parser"),
  })
  const datasets = useQuery({
    queryKey: ["canonical-datasets"],
    queryFn: () => api<CanonicalDataset[]>("/api/app/canonical-dataset"),
  })
  const remove = useMutation({
    mutationFn: (id: string) => api(`/api/app/parser/${id}`, { method: "DELETE" }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["parsers"] }),
  })
  const [query, setQuery] = useState("")
  const rows = useMemo(() => {
    const needle = query.trim().toLowerCase()
    return (parsers.data ?? []).filter((parser) => {
      const studio = readStudio(parser.pipelineJson)?.studio
      const dataset = datasets.data?.find((item) => item.id === studio?.datasetId)
      const haystack = [parser.name, parser.fileFormat, dataset?.code, dataset?.name, parser.outputGcsPrefix, parser.createdWithAi ? "ai" : "manual"].join(" ").toLowerCase()
      return !needle || haystack.includes(needle)
    })
  }, [datasets.data, parsers.data, query])

  return (
    <div className="flex flex-col gap-4">
      <div className="flex items-center justify-between gap-4">
        <p className="text-muted-foreground text-sm">Studio-managed parsers. Each one reads an uploaded file and writes a canonical dataset.</p>
        <Button size="sm" render={<Link to="/configuration/manual/parsers/new" />}>
          <Plus />
          New parser
        </Button>
      </div>
      <div className="grid gap-3 sm:grid-cols-3">
        <Stat label="Total parsers" value={parsers.data?.length ?? 0} />
        <Stat label="Active" value={parsers.data?.filter((parser) => parser.isActive).length ?? 0} />
        <Stat label="Draft" value={parsers.data?.filter((parser) => !parser.isActive).length ?? 0} />
      </div>
      {parsers.error ? <ErrorNote message={(parsers.error as Error).message} /> : null}
      {remove.error ? <ErrorNote message={(remove.error as Error).message} /> : null}
      <div className="overflow-hidden border bg-card">
        <div className="flex items-center gap-3 border-b px-3 py-3">
          <div className="relative min-w-0 flex-1">
            <Search className="text-muted-foreground absolute top-1/2 left-3 size-4 -translate-y-1/2" />
            <input className="border-input h-9 w-full border bg-background pr-3 pl-9 text-sm outline-none" value={query} placeholder="Search name, dataset, or output" onChange={(event) => setQuery(event.target.value)} />
          </div>
          <span className="text-muted-foreground text-xs tabular-nums">{rows.length} shown</span>
        </div>
        {rows.length === 0 ? (
          <div className="text-muted-foreground px-4 py-10 text-center text-sm">
            <Database className="mx-auto mb-2 size-6 opacity-40" />
            No parsers match the current search.
          </div>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="text-muted-foreground bg-muted/40 border-b text-left text-xs">
                  <th className="px-4 py-2.5 font-medium">Parser</th>
                  <th className="px-4 py-2.5 font-medium">Dataset</th>
                  <th className="px-4 py-2.5 font-medium">Output</th>
                  <th className="px-4 py-2.5 font-medium">Source</th>
                  <th className="px-4 py-2.5 font-medium">Origin</th>
                  <th className="px-4 py-2.5 font-medium">Status</th>
                  <th className="px-4 py-2.5 text-right font-medium"><span className="sr-only">Actions</span></th>
                </tr>
              </thead>
              <tbody>
                {rows.map((parser) => {
                  const studio = readStudio(parser.pipelineJson)?.studio
                  const dataset = datasets.data?.find((item) => item.id === studio?.datasetId)
                  return (
                    <tr key={parser.id} className="hover:bg-muted/30 border-b last:border-0">
                      <td className="px-4 py-3">
                        <p className="font-medium">{parser.name}</p>
                        <p className="text-muted-foreground max-w-md truncate text-xs">{studio?.description || "—"}</p>
                      </td>
                      <td className="px-4 py-3">{dataset?.code || "—"}</td>
                      <td className="text-muted-foreground max-w-xs truncate px-4 py-3 font-mono text-xs">{dataset?.outputUri || parser.outputGcsPrefix || dataset?.gcsPrefix || "—"}</td>
                      <td className="px-4 py-3">{studio?.expectedFormat || parser.fileFormat}</td>
                      <td className="px-4 py-3"><OriginCell createdWithAi={parser.createdWithAi} /></td>
                      <td className="px-4 py-3">
                        <span className="inline-flex items-center gap-2">
                          <span className={`size-1.5 ${parser.isActive ? "bg-primary" : "bg-foreground/30"}`} />
                          {parser.isActive ? "Active" : "Draft"}
                        </span>
                      </td>
                      <td className="px-4 py-3 text-right">
                        <div className="flex items-center justify-end gap-1">
                          <Button variant="ghost" size="icon-sm" aria-label={`Configure ${parser.name}`} title="Configure" render={<Link to={`/configuration/manual/parsers/${parser.id}`} />}>
                            <Settings2 />
                          </Button>
                          <Button
                            variant="ghost"
                            size="icon-sm"
                            aria-label={`Delete ${parser.name}`}
                            title="Delete parser"
                            disabled={remove.isPending}
                            onClick={() => {
                              if (window.confirm(`Delete parser "${parser.name}"? This will also remove any runs associated with it.`)) {
                                remove.mutate(parser.id)
                              }
                            }}
                          >
                            <Trash2 />
                          </Button>
                        </div>
                      </td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </div>
  )
}

function Stat({ label, value }: { label: string; value: number }) {
  return (
    <div className="border bg-card p-4">
      <p className="text-muted-foreground text-xs">{label}</p>
      <p className="mt-1 text-2xl font-medium tabular-nums">{value}</p>
    </div>
  )
}
