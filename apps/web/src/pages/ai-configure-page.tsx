import { Button } from "@workspace/ui/components/button"
import { useState } from "react"
import { Link } from "react-router"
import { inputClass, labelClass } from "@/lib/ui"

type StagedFile = {
  id: string
  file: File
}

export function AiConfigurePage() {
  const [files, setFiles] = useState<StagedFile[]>([])

  return (
    <section className="flex flex-col gap-6">
      <div>
        <Button variant="link" className="h-auto px-0" render={<Link to="/configuration" />}>
          Configuration
        </Button>
        <h1 className="text-2xl font-medium">Configure with AI</h1>
        <p className="text-muted-foreground mt-1 text-sm">Each file you add is one dataset. The assistant will draft a profile from these files.</p>
      </div>

      <form className="flex flex-col gap-4 border bg-card p-5" onSubmit={(event) => event.preventDefault()}>
        <label className="flex flex-col gap-1.5">
          <span className={labelClass}>Dataset files</span>
          <input
            className={inputClass}
            type="file"
            accept=".csv,text/csv"
            multiple
            onChange={(event) => {
              const selected = Array.from(event.target.files ?? [])
              setFiles((current) => [
                ...current,
                ...selected.map((file) => ({ id: `${file.name}-${file.size}-${file.lastModified}`, file })),
              ])
              event.target.value = ""
            }}
          />
        </label>

        {files.length === 0 ? <p className="text-muted-foreground text-sm">No datasets added yet.</p> : null}

        <ul className="flex flex-col gap-2">
          {files.map((item) => (
            <li key={item.id} className="flex items-center justify-between border px-3 py-2">
              <div>
                <p className="text-sm font-medium">{datasetName(item.file.name)}</p>
                <p className="text-muted-foreground text-xs">{item.file.name}</p>
              </div>
              <Button
                type="button"
                variant="outline"
                size="sm"
                onClick={() => setFiles((current) => current.filter((entry) => entry.id !== item.id))}
              >
                Remove
              </Button>
            </li>
          ))}
        </ul>
      </form>
    </section>
  )
}

function datasetName(fileName: string) {
  const stem = fileName.replace(/\.csv$/i, "")
  return stem.replace(/[^A-Za-z0-9_]+/g, "_").replace(/^_+|_+$/g, "").toUpperCase() || "DATASET"
}
