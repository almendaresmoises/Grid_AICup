import { api } from "@/lib/api"

export type UserIdentity = {
  id?: string | null
  displayName?: string | null
  userName?: string | null
  name?: string | null
  surName?: string | null
  surname?: string | null
}

export async function getCurrentUser() {
  const configuration = await api<{ currentUser: UserIdentity }>(
    "/api/abp/application-configuration"
  )
  return configuration.currentUser
}

export function userDisplayName(user?: UserIdentity) {
  if (!user) return null
  const fullName = [user.name, user.surName ?? user.surname]
    .map((part) => part?.trim())
    .filter(Boolean)
    .join(" ")
  return user.displayName?.trim() || fullName || user.userName?.trim() || null
}

export function shortUserId(id: string | null) {
  if (!id) return "—"
  return id.length > 12 ? `${id.slice(0, 8)}…${id.slice(-4)}` : id
}
