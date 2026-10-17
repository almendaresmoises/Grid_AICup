import { Button } from "@workspace/ui/components/button"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { Pencil, Plus, Trash2 } from "lucide-react"
import { useState } from "react"
import { ErrorNote } from "@/components/error-note"
import { Modal } from "@/components/modal"
import { OriginCell } from "@/components/origin-cell"
import { ApiError, api } from "@/lib/api"
import {
  columnTypes,
  parseColumns,
  parsePartitions,
  semanticRoles,
  type CanonicalDataset,
  type SchemaColumn,
} from "@/lib/canonical"
import { inputClass, labelClass } from "@/lib/ui"

const emptyColumn = (): SchemaColumn => ({ name: "", polarsType: "pl.String", isRequired: true, semanticRole: "none" })

export function DatasetsPage() {
  const queryClient = useQueryClient()
  const datasets = useQuery({
    queryKey: ["canonical-datasets"],
    queryFn: () => api<CanonicalDataset[]>("/api/app/canonical-dataset"),
  })
  const [editing, setEditing] = useState<CanonicalDataset | "new" | null>(null)
  const remove = useMutation({
    mutationFn: (id: string) => api(`/api/app/canonical-dataset/${id}`, { method: "DELETE" }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["canonical-datasets"] }),
  })

  return (
    <div className="flex flex-col gap-4">
      <div className="flex items-center justify-between gap-4">
        <p className="text-muted-foreground text-sm">Canonical schemas and the Cloud Storage directory each parser writes to.</p>
        <Button size="sm" type="button" onClick={() => setEditing("new")}>
          <Plus />
          New dataset
        </Button>
      </div>
      {datasets.error ? <ErrorNote message={(datasets.error as Error).message} /> : null}
      {remove.error ? <ErrorNote message={(remove.error as Error).message} /> : null}
      {editing ? (
        <DatasetForm
          dataset={editing === "new" ? undefined : editing}
          onClose={() => setEditing(null)}
        />
      ) : null}
      <div className="overflow-hidden border bg-card">
        <div className="border-b px-4 py-3">
          <p className="text-sm font-medium">{datasets.data?.length ?? 0} {datasets.data?.length === 1 ? "dataset" : "datasets"}</p>
        </div>
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead>
              <tr className="text-muted-foreground bg-muted/40 border-b text-left text-xs">
                <th className="px-4 py-2.5 font-medium">Code</th>
                <th className="px-4 py-2.5 font-medium">Name</th>
                <th className="px-4 py-2.5 font-medium">Columns</th>
                <th className="px-4 py-2.5 font-medium">Output</th>
                <th className="px-4 py-2.5 font-medium">Origin</th>
                <th className="px-4 py-2.5 text-right font-medium">
                  <span className="sr-only">Actions</span>
                </th>
              </tr>
            </thead>
            <tbody>
              {datasets.data?.length === 0 ? (
                <tr>
                  <td colSpan={6} className="text-muted-foreground px-4 py-8 text-center">
                    No canonical datasets yet.
                  </td>
                </tr>
              ) : null}
              {datasets.data?.map((dataset) => (
                <tr key={dataset.id} className="hover:bg-muted/30 border-b last:border-0">
                  <td className="px-4 py-3 font-mono text-xs">{dataset.code}</td>
                  <td className="px-4 py-3 font-medium">{dataset.name}</td>
                  <td className="px-4 py-3 tabular-nums">{parseColumns(dataset.schemaColumnsJson).length}</td>
                  <td className="text-muted-foreground px-4 py-3 font-mono text-xs">{dataset.outputUri || dataset.gcsPrefix}</td>
                  <td className="px-4 py-3"><OriginCell createdWithAi={dataset.createdWithAi} /></td>
                  <td className="px-4 py-3 text-right">
                    <Button variant="ghost" size="icon-sm" aria-label={`Edit ${dataset.code}`} title="Edit" onClick={() => setEditing(dataset)}>
                      <Pencil />
                    </Button>
                    <Button variant="ghost" size="icon-sm" aria-label={`Delete ${dataset.code}`} title="Delete" onClick={() => remove.mutate(dataset.id)}>
                      <Trash2 />
                    </Button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  )
}

function DatasetForm({ dataset, onClose }: { dataset?: CanonicalDataset; onClose: () => void }) {
  const queryClient = useQueryClient()
  const [code, setCode] = useState(dataset?.code ?? "")
  const [name, setName] = useState(dataset?.name ?? "")
  const [description, setDescription] = useState(dataset?.description ?? "")
  const [gcsPrefix, setGcsPrefix] = useState(dataset?.gcsPrefix ?? "")
  const [columns, setColumns] = useState<SchemaColumn[]>(dataset ? parseColumns(dataset.schemaColumnsJson) : [emptyColumn()])
  const [partitions, setPartitions] = useState(dataset ? parsePartitions(dataset.partitionColumnsJson).join(", ") : "")
  const [error, setError] = useState("")

  const save = useMutation({
    mutationFn: () => {
      const body = JSON.stringify({
        code,
        name,
        description,
        gcsPrefix,
        schemaColumnsJson: JSON.stringify(columns),
        partitionColumnsJson: JSON.stringify(
          partitions
            .split(",")
            .map((item) => item.trim())
            .filter(Boolean),
        ),
      })
      return dataset
        ? api<CanonicalDataset>(`/api/app/canonical-dataset/${dataset.id}`, { method: "PUT", body })
        : api<CanonicalDataset>("/api/app/canonical-dataset", { method: "POST", body })
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["canonical-datasets"] })
      onClose()
    },
    onError: (caught) => setError(caught instanceof ApiError ? caught.message : "Could not save the dataset"),
  })

  return (
    <Modal
      isOpen={true}
      onClose={onClose}
      title={dataset ? `Edit dataset: ${dataset.code}` : "New canonical dataset"}
      description="Define schema columns, data types, partition keys, and storage directory."
      size="3xl"
      footer={
        <div className="flex items-center justify-end gap-2">
          <Button type="button" variant="outline" size="sm" onClick={onClose}>
            Cancel
          </Button>
          <Button
            type="submit"
            form="dataset-form"
            size="sm"
            disabled={save.isPending}
          >
            {save.isPending ? "Saving…" : "Save dataset"}
          </Button>
        </div>
      }
    >
      <form
        id="dataset-form"
        className="flex flex-col gap-4"
        onSubmit={(event) => {
          event.preventDefault()
          setError("")
          save.mutate()
        }}
      >
        {error ? <ErrorNote message={error} /> : null}
        <div className="grid gap-4 sm:grid-cols-2">
          <label className="flex flex-col gap-1.5">
            <span className={labelClass}>Code</span>
            <input className={inputClass} value={code} onChange={(event) => setCode(event.target.value)} required />
          </label>
          <label className="flex flex-col gap-1.5">
            <span className={labelClass}>Name</span>
            <input className={inputClass} value={name} onChange={(event) => setName(event.target.value)} required />
          </label>
          <label className="flex flex-col gap-1.5">
            <span className={labelClass}>Cloud Storage directory</span>
            <input className={inputClass} value={gcsPrefix} onChange={(event) => setGcsPrefix(event.target.value)} required />
          </label>
          <label className="flex flex-col gap-1.5">
            <span className={labelClass}>Partitions</span>
            <input className={inputClass} value={partitions} onChange={(event) => setPartitions(event.target.value)} placeholder="year, month, day" />
          </label>
        </div>
        <label className="flex flex-col gap-1.5">
          <span className={labelClass}>Description</span>
          <input className={inputClass} value={description} onChange={(event) => setDescription(event.target.value)} />
        </label>
        <div className="flex flex-col gap-2">
          <div className="flex items-center justify-between">
            <span className={labelClass}>Schema</span>
            <Button type="button" variant="outline" size="sm" onClick={() => setColumns([...columns, emptyColumn()])}>
              Add column
            </Button>
          </div>
          {columns.map((column, index) => (
            <div key={index} className="grid gap-2 sm:grid-cols-[1fr_180px_140px_auto_auto] sm:items-center">
              <input
                className={inputClass}
                aria-label={`Column ${index + 1} name`}
                value={column.name}
                placeholder="column_name"
                onChange={(event) => updateColumn(index, { name: event.target.value })}
              />
              <select
                className={inputClass}
                aria-label={`Column ${index + 1} type`}
                value={column.polarsType}
                onChange={(event) => updateColumn(index, { polarsType: event.target.value })}
              >
                {columnTypes.map((type) => (
                  <option key={type} value={type}>
                    {type}
                  </option>
                ))}
              </select>
              <select
                className={inputClass}
                aria-label={`Column ${index + 1} role`}
                value={column.semanticRole}
                onChange={(event) => updateColumn(index, { semanticRole: event.target.value })}
              >
                {semanticRoles.map((role) => (
                  <option key={role.value} value={role.value}>
                    {role.label}
                  </option>
                ))}
              </select>
              <label className="flex items-center gap-2 text-sm">
                <input
                  type="checkbox"
                  checked={column.isRequired}
                  onChange={(event) => updateColumn(index, { isRequired: event.target.checked })}
                />
                Required
              </label>
              <Button type="button" variant="ghost" size="sm" onClick={() => setColumns(columns.filter((_, item) => item !== index))}>
                Remove
              </Button>
            </div>
          ))}
        </div>
      </form>
    </Modal>
  )

  function updateColumn(index: number, patch: Partial<SchemaColumn>) {
    setColumns(columns.map((column, item) => (item === index ? { ...column, ...patch } : column)))
  }
}
