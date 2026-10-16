import { Button } from "@workspace/ui/components/button"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { api } from "@/lib/api"
import type { Dataset, DatasetColumn } from "@/lib/types"
import { ErrorNote } from "@/components/error-note"

export function DatasetsPage() {
  const queryClient = useQueryClient()
  const datasets = useQuery({
    queryKey: ["datasets"],
    queryFn: () => api<Dataset[]>("/api/app/dataset"),
  })
  const remove = useMutation({
    mutationFn: (id: string) => api(`/api/app/dataset/${id}`, { method: "DELETE" }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["datasets"] }),
  })
  const updateColumns = useMutation({
    mutationFn: (input: { id: string; columns: DatasetColumn[] }) =>
      api<Dataset>(`/api/app/dataset/${input.id}`, {
        method: "PUT",
        body: JSON.stringify({ columns: input.columns }),
      }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["datasets"] }),
  })

  return (
    <section className="flex flex-col gap-4">
      <div>
        <h1 className="text-2xl font-medium">Datasets</h1>
        <p className="text-muted-foreground mt-1 text-sm">
          Column types describe the file. Match rules choose their own text, amount, or date normalization.
        </p>
      </div>
      {datasets.error ? <ErrorNote message={(datasets.error as Error).message} /> : null}
      {datasets.data?.length === 0 ? <p className="text-muted-foreground text-sm">No datasets yet.</p> : null}
      <div className="flex flex-col gap-4">
        {datasets.data?.map((dataset) => (
          <article key={dataset.id} className="border bg-card p-4">
            <div className="flex items-start justify-between gap-4">
              <div>
                <h2 className="font-medium">
                  {dataset.code} <span className="text-muted-foreground font-normal">{dataset.name}</span>
                </h2>
                <p className="text-muted-foreground text-sm">{dataset.rowCount.toLocaleString()} rows</p>
              </div>
              <Button variant="outline" size="sm" onClick={() => remove.mutate(dataset.id)}>
                Delete
              </Button>
            </div>
            <div className="mt-4 flex flex-wrap gap-3">
              {dataset.columns.map((column) => (
                <label key={column.name} className="flex items-center gap-2 text-sm">
                  <span className="font-mono text-xs">{column.name}</span>
                  <select
                    className="h-8 border border-input bg-background px-2 text-sm"
                    value={column.type}
                    onChange={(event) => {
                      const columns = dataset.columns.map((item) =>
                        item.name === column.name ? { ...item, type: event.target.value } : item,
                      )
                      updateColumns.mutate({ id: dataset.id, columns })
                    }}
                  >
                    <option value="text">text</option>
                    <option value="amount">amount</option>
                    <option value="date">date</option>
                  </select>
                </label>
              ))}
            </div>
          </article>
        ))}
      </div>
    </section>
  )
}
