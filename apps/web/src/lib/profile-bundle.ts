import { api } from "./api"
import type { Profile, ProfileBundle } from "./types"

export function downloadJson(filename: string, data: unknown) {
  const text = typeof data === "string" ? data : JSON.stringify(data, null, 2)
  const blob = new Blob([text], { type: "application/json;charset=utf-8" })
  const url = URL.createObjectURL(blob)
  const link = document.createElement("a")
  link.href = url
  link.download = filename
  link.click()
  URL.revokeObjectURL(url)
}

export function normalizeProfileBundle(input: unknown): ProfileBundle {
  const raw = typeof input === "string" ? JSON.parse(input) : input
  if (!raw || typeof raw !== "object") {
    throw new Error("Invalid JSON: Expected an object representing a bundle or profile definition.")
  }

  const record = raw as Record<string, unknown>

  // Check if it's already a ProfileBundle
  if (record.profile && typeof record.profile === "object") {
    const profile = record.profile as Record<string, unknown>
    let definitionJson = typeof profile.definitionJson === "string" ? profile.definitionJson : ""
    if (!definitionJson && profile.definition && typeof profile.definition === "object") {
      definitionJson = JSON.stringify(profile.definition, null, 2)
    }

    const datasets = Array.isArray(record.datasets)
      ? record.datasets.map((d: Record<string, unknown>) => ({
          code: String(d.code || ""),
          name: String(d.name || d.code || ""),
          description: d.description ? String(d.description) : "",
          gcsPrefix: String(d.gcsPrefix || ""),
          schemaColumnsJson:
            typeof d.schemaColumnsJson === "string"
              ? d.schemaColumnsJson
              : d.schemaColumns
              ? JSON.stringify(d.schemaColumns)
              : "[]",
          partitionColumnsJson:
            typeof d.partitionColumnsJson === "string"
              ? d.partitionColumnsJson
              : d.partitionColumns
              ? JSON.stringify(d.partitionColumns)
              : "[]",
        }))
      : []

    const parsers = Array.isArray(record.parsers)
      ? record.parsers.map((p: Record<string, unknown>) => ({
          name: String(p.name || ""),
          fileFormat: String(p.fileFormat || "csv"),
          pipelineJson:
            typeof p.pipelineJson === "string"
              ? p.pipelineJson
              : p.pipeline
              ? JSON.stringify(p.pipeline)
              : "{}",
          outputGcsPrefix: String(p.outputGcsPrefix || ""),
          isActive: p.isActive !== false,
          createdWithAi: Boolean(p.createdWithAi),
        }))
      : []

    return {
      formatVersion: Number(record.formatVersion) || 1,
      exportedAt: record.exportedAt ? String(record.exportedAt) : new Date().toISOString(),
      package: record.package && typeof record.package === "object"
        ? {
            name: String((record.package as Record<string, unknown>).name || profile.name || "Imported Package"),
            description: (record.package as Record<string, unknown>).description
              ? String((record.package as Record<string, unknown>).description)
              : null,
          }
        : {
            name: String(profile.name || "Imported Package"),
            description: profile.description ? String(profile.description) : null,
          },
      profile: {
        name: String(profile.name || "Imported Profile"),
        description: profile.description ? String(profile.description) : null,
        version: Number(profile.version) || 0,
        status: String(profile.status || "Draft"),
        definitionJson: definitionJson || "{}",
        createdWithAi: Boolean(profile.createdWithAi),
      },
      datasets,
      parsers,
    }
  }

  // Check if it's a raw ProfileDefinition (contains "rules" and "datasets")
  if (Array.isArray(record.rules) || Array.isArray(record.datasets)) {
    const name = String(record.name || "Imported Profile")
    return {
      formatVersion: 1,
      exportedAt: new Date().toISOString(),
      package: {
        name,
        description: "Reconciliation package created from JSON definition",
      },
      profile: {
        name,
        version: Number(record.version) || 0,
        status: "Draft",
        definitionJson: JSON.stringify(record, null, 2),
      },
      datasets: [],
      parsers: [],
    }
  }

  throw new Error(
    "Unrecognized JSON structure. Please provide a Grid profile bundle or a valid profile definition with 'rules' and 'datasets'."
  )
}

export function downloadProfileBundle(bundle: ProfileBundle, nameHint?: string) {
  const safeName = (nameHint || bundle.profile?.name || bundle.package?.name || "profile")
    .replace(/[^a-z0-9_-]/gi, "_")
    .toLowerCase()
  downloadJson(`${safeName}-bundle.json`, bundle)
}

export async function exportProfileBundle(profileId: string, nameHint?: string): Promise<ProfileBundle> {
  const bundle = await api<ProfileBundle>(`/api/app/profile/${profileId}/bundle`)
  downloadProfileBundle(bundle, nameHint)
  return bundle
}

export async function importProfileBundle(payload: unknown): Promise<Profile> {
  const bundle = normalizeProfileBundle(payload)
  return api<Profile>("/api/app/profile/import-bundle", {
    method: "POST",
    body: JSON.stringify(bundle),
  })
}
