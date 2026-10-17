export type DashboardDay = {
  date: string
  completedRuns: number
  totalRecords: number
  fullMatchRecords: number
  partialMatchRecords: number
  exceptionRecords: number
}

export type DashboardProfile = {
  profileId: string
  profileName: string
  completedRuns: number
  totalRecords: number
  fullMatchRecords: number
  partialMatchRecords: number
  exceptionRecords: number
  matchRate: number
}

export type Dashboard = {
  fromDate: string
  throughDate: string
  timeZone: string
  generatedAtUtc: string
  completedRuns: number
  totalRecords: number
  fullMatchRecords: number
  partialMatchRecords: number
  exceptionRecords: number
  matchRate: number | null
  days: DashboardDay[]
  lowestMatchProfiles: DashboardProfile[]
}
