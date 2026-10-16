import { Button } from "@workspace/ui/components/button"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { useState } from "react"
import { Link } from "react-router"
import { api } from "@/lib/api"
import type { Package } from "@/lib/types"
import { ErrorNote } from "@/components/error-note"
import { inputClass, labelClass, textareaClass } from "@/lib/ui"

export function PackagesPage() {
  const queryClient = useQueryClient()
  const [name, setName] = useState("")
  const [description, setDescription] = useState("")
  const packages = useQuery({
    queryKey: ["packages"],
    queryFn: () => api<Package[]>("/api/app/package"),
  })
  const create = useMutation({
    mutationFn: () =>
      api<Package>("/api/app/package", {
        method: "POST",
        body: JSON.stringify({ name, description }),
      }),
    onSuccess: async () => {
      setName("")
      setDescription("")
      await queryClient.invalidateQueries({ queryKey: ["packages"] })
    },
  })

  return (
    <section className="flex flex-col gap-6">
      <div>
        <h1 className="text-2xl font-medium">Packages</h1>
        <p className="text-muted-foreground mt-1 text-sm">
          A package holds one reconciliation profile. Approve it, then run it against the current datasets.
        </p>
      </div>
      <form
        className="flex flex-col gap-3 border bg-card p-5"
        onSubmit={(event) => {
          event.preventDefault()
          create.mutate()
        }}
      >
        <label className="flex flex-col gap-1.5">
          <span className={labelClass}>Name</span>
          <input className={inputClass} value={name} onChange={(event) => setName(event.target.value)} />
        </label>
        <label className="flex flex-col gap-1.5">
          <span className={labelClass}>Description</span>
          <textarea className={textareaClass} value={description} onChange={(event) => setDescription(event.target.value)} />
        </label>
        {create.error ? <ErrorNote message={(create.error as Error).message} /> : null}
        <div>
          <Button type="submit" disabled={create.isPending}>
            Create package
          </Button>
        </div>
      </form>
      {packages.error ? <ErrorNote message={(packages.error as Error).message} /> : null}
      <div className="flex flex-col gap-3">
        {packages.data?.map((item) => (
          <article key={item.id} className="flex items-center justify-between border bg-card px-4 py-3">
            <div>
              <h2 className="font-medium">{item.name}</h2>
              <p className="text-muted-foreground text-sm">
                {item.profileStatus} · version {item.profileVersion}
                {item.description ? ` · ${item.description}` : ""}
              </p>
            </div>
            <Button variant="outline" size="sm" render={<Link to={`/reconciliation/profiles/${item.profileId}`} />}>
              Open profile
            </Button>
          </article>
        ))}
      </div>
    </section>
  )
}
