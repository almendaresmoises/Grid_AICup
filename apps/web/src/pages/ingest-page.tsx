import { Button } from "@workspace/ui/components/button"
import { useMutation, useQueryClient } from "@tanstack/react-query"
import { useState } from "react"
import { ApiError, api } from "@/lib/api"
import type { Dataset } from "@/lib/types"
import { ErrorNote } from "@/components/error-note"
import { inputClass, labelClass } from "@/lib/ui"

export function IngestPage() {
  const queryClient = useQueryClient()
  const [code, setCode] = useState("")
  const [name, setName] = useState("")
  const [file, setFile] = useState<File | null>(null)
  const [saved, setSaved] = useState<Dataset | null>(null)
  const [error, setError] = useState("")

  const upload = useMutation({
    mutationFn: async () => {
      if (!file) {
        throw new ApiError("Choose a CSV file")
      }
      const csvText = await file.text()
      return api<Dataset>("/api/app/dataset", {
        method: "POST",
        body: JSON.stringify({ code, name, csvText }),
      })
    },
    onSuccess: async (dataset) => {
      setSaved(dataset)
      setError("")
      await queryClient.invalidateQueries({ queryKey: ["datasets"] })
    },
    onError: (caught) => setError(caught instanceof Error ? caught.message : "Upload failed"),
  })

  return (
    <section className="flex flex-col gap-6">
      <div>
        <h1 className="text-2xl font-medium">Ingest</h1>
        <p className="text-muted-foreground mt-1 text-sm">
          Upload 2 or 3 CSVs. Each file becomes a named dataset. Uploading the same code replaces its rows.
        </p>
      </div>
      <form
        className="grid gap-4 border bg-card p-5 md:grid-cols-2"
        onSubmit={(event) => {
          event.preventDefault()
          upload.mutate()
        }}
      >
        <label className="flex flex-col gap-1.5">
          <span className={labelClass}>Dataset code</span>
          <input
            className={inputClass}
            value={code}
            placeholder="POS"
            onChange={(event) => setCode(event.target.value.toUpperCase())}
          />
        </label>
        <label className="flex flex-col gap-1.5">
          <span className={labelClass}>Name</span>
          <input className={inputClass} value={name} placeholder="Point of sale" onChange={(event) => setName(event.target.value)} />
        </label>
        <label className="flex flex-col gap-1.5 md:col-span-2">
          <span className={labelClass}>CSV file</span>
          <input
            className={inputClass}
            type="file"
            accept=".csv,text/csv"
            onChange={(event) => setFile(event.target.files?.[0] ?? null)}
          />
        </label>
        {error ? (
          <div className="md:col-span-2">
            <ErrorNote message={error} />
          </div>
        ) : null}
        <div>
          <Button type="submit" disabled={upload.isPending}>
            {upload.isPending ? "Saving…" : "Save dataset"}
          </Button>
        </div>
      </form>
      {saved ? (
        <div className="flex flex-col gap-3">
          <h2 className="text-lg font-medium">
            {saved.code} · {saved.rowCount.toLocaleString()} rows
          </h2>
          <Preview columns={saved.columns.map((column) => column.name)} rows={saved.sampleRows ?? []} />
        </div>
      ) : null}
    </section>
  )
}

export function Preview({ columns, rows }: { columns: string[]; rows: Record<string, string>[] }) {
  if (rows.length === 0) {
    return <p className="text-muted-foreground text-sm">No sample rows.</p>
  }

  return (
    <div className="overflow-x-auto border">
      <table className="w-full text-left text-sm">
        <thead className="bg-muted">
          <tr>
            {columns.map((column) => (
              <th key={column} className="px-3 py-2 font-medium">
                {column}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.map((row, index) => (
            <tr key={index} className="border-t">
              {columns.map((column) => (
                <td key={column} className="px-3 py-2 font-mono text-xs">
                  {row[column]}
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}
