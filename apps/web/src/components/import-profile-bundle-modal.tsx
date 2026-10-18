import { useState, useId, useMemo, useRef } from "react"
import { useMutation, useQueryClient } from "@tanstack/react-query"
import { useNavigate } from "react-router"
import { Button } from "@workspace/ui/components/button"
import { AlertCircle, CheckCircle2, Database, FileCode2, FileText, Loader2, Sparkles, Upload } from "lucide-react"
import { Modal } from "./modal"
import { ErrorNote } from "./error-note"
import { importProfileBundle, normalizeProfileBundle } from "@/lib/profile-bundle"
import { parseProfile, type Profile } from "@/lib/types"

interface ImportProfileBundleModalProps {
  isOpen: boolean
  onClose: () => void
  onSuccess?: (profile: Profile) => void
}

export function ImportProfileBundleModal({ isOpen, onClose, onSuccess }: ImportProfileBundleModalProps) {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const fileInputRef = useRef<HTMLInputElement>(null)
  const inputId = useId()

  const [jsonText, setJsonText] = useState("")
  const [fileName, setFileName] = useState<string | null>(null)
  const [isDragging, setIsDragging] = useState(false)

  // Parse and preview bundle
  const parsedState = useMemo(() => {
    if (!jsonText.trim()) {
      return { bundle: null, spec: null, error: null }
    }
    try {
      const bundle = normalizeProfileBundle(jsonText)
      const spec = parseProfile(bundle.profile.definitionJson)
      return { bundle, spec, error: null }
    } catch (err) {
      return {
        bundle: null,
        spec: null,
        error: err instanceof Error ? err.message : "Failed to parse JSON",
      }
    }
  }, [jsonText])

  const handleFile = async (file: File) => {
    try {
      const text = await file.text()
      setJsonText(text)
      setFileName(file.name)
    } catch {
      // Ignored
    }
  }

  const importMutation = useMutation({
    mutationFn: async () => {
      if (!parsedState.bundle) {
        throw new Error("No valid JSON bundle to import.")
      }
      return importProfileBundle(parsedState.bundle)
    },
    onSuccess: async (createdProfile) => {
      await queryClient.invalidateQueries({ queryKey: ["packages"] })
      await queryClient.invalidateQueries({ queryKey: ["profile-registry"] })
      await queryClient.invalidateQueries({ queryKey: ["canonical-dataset"] })
      await queryClient.invalidateQueries({ queryKey: ["parsers"] })
      onClose()
      if (onSuccess) {
        onSuccess(createdProfile)
      } else {
        navigate(`/reconciliation/profiles/${createdProfile.id}`)
      }
    },
  })

  const reset = () => {
    setJsonText("")
    setFileName(null)
    importMutation.reset()
  }

  const handleClose = () => {
    reset()
    onClose()
  }

  return (
    <Modal
      isOpen={isOpen}
      onClose={handleClose}
      title={
        <div className="flex items-center gap-2">
          <FileCode2 className="size-5 text-primary" />
          <span>Create Profile from JSON</span>
        </div>
      }
      description="Import a profile bundle with attached datasets and parsers, or paste a raw profile definition."
      size="2xl"
      footer={
        <div className="flex items-center justify-between w-full">
          <div className="text-xs text-muted-foreground">
            {parsedState.bundle ? (
              <span className="flex items-center gap-1.5 text-emerald-600 dark:text-emerald-400">
                <CheckCircle2 className="size-3.5" />
                Ready to import & provision
              </span>
            ) : jsonText.trim() ? (
              <span className="flex items-center gap-1.5 text-destructive">
                <AlertCircle className="size-3.5" />
                Invalid JSON format
              </span>
            ) : (
              <span>Upload a .json bundle or paste JSON below</span>
            )}
          </div>
          <div className="flex items-center gap-2">
            <Button type="button" variant="outline" size="sm" onClick={handleClose}>
              Cancel
            </Button>
            <Button
              type="button"
              size="sm"
              disabled={importMutation.isPending || !parsedState.bundle}
              onClick={() => importMutation.mutate()}
            >
              {importMutation.isPending ? (
                <>
                  <Loader2 className="size-3.5 animate-spin" />
                  Importing…
                </>
              ) : (
                <>
                  <Upload className="size-3.5" />
                  Import & Open Studio
                </>
              )}
            </Button>
          </div>
        </div>
      }
    >
      <div className="flex flex-col gap-4">
        {importMutation.error ? (
          <ErrorNote message={(importMutation.error as Error).message} />
        ) : null}

        {/* File Drag and Drop / Upload zone */}
        <div
          onDragOver={(e) => {
            e.preventDefault()
            setIsDragging(true)
          }}
          onDragLeave={() => setIsDragging(false)}
          onDrop={(e) => {
            e.preventDefault()
            setIsDragging(false)
            if (e.dataTransfer.files?.[0]) {
              handleFile(e.dataTransfer.files[0])
            }
          }}
          className={`flex flex-col items-center justify-center gap-2 rounded-lg border border-dashed p-4 text-center transition-colors ${
            isDragging
              ? "border-primary bg-primary/5"
              : "border-border hover:border-primary/50 hover:bg-muted/30"
          }`}
        >
          <input
            id={inputId}
            ref={fileInputRef}
            type="file"
            accept=".json,application/json"
            className="hidden"
            onChange={(e) => {
              if (e.target.files?.[0]) {
                handleFile(e.target.files[0])
              }
            }}
          />
          <div className="flex items-center gap-2">
            <FileText className="size-5 text-muted-foreground" />
            <span className="text-sm font-medium">
              {fileName ? fileName : "Drop your bundle .json file here, or"}
            </span>
            <Button
              type="button"
              variant="outline"
              size="sm"
              onClick={() => fileInputRef.current?.click()}
            >
              Choose file
            </Button>
          </div>
          <p className="text-xs text-muted-foreground">
            Supports exported Grid bundles or raw profile JSON definitions.
          </p>
        </div>

        {/* Inspected Bundle Preview */}
        {parsedState.bundle ? (
          <div className="rounded-lg border bg-muted/20 p-3.5 text-xs flex flex-col gap-2.5">
            <div className="flex flex-wrap items-center justify-between gap-2 border-b pb-2">
              <div className="flex items-center gap-2">
                <span className="font-semibold text-foreground text-sm">
                  {parsedState.bundle.profile.name}
                </span>
                <span className="rounded bg-muted px-1.5 py-0.5 font-mono text-[10px]">
                  Version {parsedState.bundle.profile.version}
                </span>
                <span className="rounded border px-1.5 py-0.5 text-[10px]">
                  {parsedState.bundle.profile.status}
                </span>
              </div>
              {parsedState.bundle.profile.createdWithAi ? (
                <span className="flex items-center gap-1 text-primary text-[10px] font-medium">
                  <Sparkles className="size-3" /> Created with AI
                </span>
              ) : null}
            </div>

            <div className="grid gap-2 sm:grid-cols-3">
              <div className="flex flex-col gap-1">
                <span className="text-muted-foreground font-medium flex items-center gap-1">
                  <Database className="size-3" /> Datasets ({parsedState.bundle.datasets.length || parsedState.spec?.datasets.length || 0})
                </span>
                <div className="flex flex-wrap gap-1">
                  {parsedState.bundle.datasets.length > 0 ? (
                    parsedState.bundle.datasets.map((d) => (
                      <span key={d.code} className="rounded bg-background border px-1.5 py-0.5 font-mono text-[10px]">
                        {d.code}
                      </span>
                    ))
                  ) : parsedState.spec?.datasets.length ? (
                    parsedState.spec.datasets.map((code) => (
                      <span key={code} className="rounded bg-background border px-1.5 py-0.5 font-mono text-[10px]">
                        {code}
                      </span>
                    ))
                  ) : (
                    <span className="text-muted-foreground italic">None attached</span>
                  )}
                </div>
              </div>

              <div className="flex flex-col gap-1">
                <span className="text-muted-foreground font-medium">
                  Parsers ({parsedState.bundle.parsers.length})
                </span>
                <div className="flex flex-wrap gap-1">
                  {parsedState.bundle.parsers.length > 0 ? (
                    parsedState.bundle.parsers.map((p) => (
                      <span key={p.name} className="rounded bg-background border px-1.5 py-0.5 text-[10px] truncate max-w-[140px]" title={p.name}>
                        {p.name}
                      </span>
                    ))
                  ) : (
                    <span className="text-muted-foreground italic">None attached</span>
                  )}
                </div>
              </div>

              <div className="flex flex-col gap-1">
                <span className="text-muted-foreground font-medium">
                  Matching Rules ({parsedState.spec?.rules.length || 0})
                </span>
                <div className="flex flex-wrap gap-1">
                  {parsedState.spec?.rules.slice(0, 4).map((r) => (
                    <span key={r.id} className="rounded bg-background border px-1.5 py-0.5 font-mono text-[10px]">
                      {r.id}
                    </span>
                  ))}
                  {(parsedState.spec?.rules.length || 0) > 4 ? (
                    <span className="text-muted-foreground text-[10px]">
                      +{(parsedState.spec?.rules.length || 0) - 4} more
                    </span>
                  ) : null}
                </div>
              </div>
            </div>
          </div>
        ) : parsedState.error ? (
          <div className="rounded-lg border border-destructive/30 bg-destructive/5 p-3 text-xs text-destructive flex items-center gap-2">
            <AlertCircle className="size-4 shrink-0" />
            <span>{parsedState.error}</span>
          </div>
        ) : null}

        {/* Raw JSON Editor / Paste Area */}
        <div className="flex flex-col gap-1.5">
          <div className="flex items-center justify-between">
            <label htmlFor="bundle-json-textarea" className="text-xs font-medium text-muted-foreground">
              JSON Content
            </label>
            {jsonText ? (
              <button
                type="button"
                className="text-xs text-muted-foreground hover:text-foreground underline"
                onClick={() => {
                  setJsonText("")
                  setFileName(null)
                }}
              >
                Clear
              </button>
            ) : null}
          </div>
          <textarea
            id="bundle-json-textarea"
            className="h-44 w-full rounded-md border border-input bg-background p-2.5 font-mono text-xs focus:border-primary focus:outline-none"
            placeholder={`{\n  "formatVersion": 1,\n  "profile": {\n    "name": "My Reconciliation Profile",\n    ...\n  },\n  "datasets": [...],\n  "parsers": [...]\n}`}
            value={jsonText}
            onChange={(e) => {
              setJsonText(e.target.value)
              if (fileName) setFileName(null)
            }}
          />
        </div>
      </div>
    </Modal>
  )
}
