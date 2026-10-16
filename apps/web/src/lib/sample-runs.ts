export type RunStatus = "Success" | "Ongoing" | "Failed"

export type SampleRun = {
  id: string
  profile: string
  status: RunStatus
  matchPercent: number | null
  openItems: number | null
  ranBy: string
  started: string
}

export type GridRow = {
  group: string
  status: "Matched" | "Open" | "Ambiguous"
  dataset: string
  reference: string
  amount: number
  date: string
  variance: string
}

export const sampleRuns: SampleRun[] = [
  {
    id: "run-1042",
    profile: "Three-way POS / processor / bank",
    status: "Success",
    matchPercent: 98.4,
    openItems: 283,
    ranBy: "admin",
    started: "16 Oct 2026, 18:40",
  },
  {
    id: "run-1041",
    profile: "Three-way POS / processor / bank",
    status: "Ongoing",
    matchPercent: 64.1,
    openItems: 1204,
    ranBy: "maria.santos",
    started: "16 Oct 2026, 19:05",
  },
  {
    id: "run-1038",
    profile: "Card settlement",
    status: "Failed",
    matchPercent: null,
    openItems: null,
    ranBy: "james.okonkwo",
    started: "15 Oct 2026, 09:12",
  },
  {
    id: "run-1033",
    profile: "Bank deposit match",
    status: "Success",
    matchPercent: 99.1,
    openItems: 42,
    ranBy: "admin",
    started: "14 Oct 2026, 21:18",
  },
  {
    id: "run-1029",
    profile: "Store cash-up",
    status: "Success",
    matchPercent: 96.7,
    openItems: 118,
    ranBy: "priya.nair",
    started: "13 Oct 2026, 22:04",
  },
]

const posGrid: GridRow[] = [
  { group: "G-1001", status: "Matched", dataset: "POS", reference: "G-1001", amount: 120, date: "14 Oct 2026", variance: "$0" },
  { group: "G-1001", status: "Matched", dataset: "Card processor", reference: "CARD/G-1001", amount: 120, date: "14 Oct 2026", variance: "$0" },
  { group: "G-1001", status: "Matched", dataset: "Bank", reference: "DEP-1001", amount: 120, date: "15 Oct 2026", variance: "$0" },
  { group: "G-1002", status: "Matched", dataset: "POS", reference: "G-1002", amount: 250, date: "14 Oct 2026", variance: "$0" },
  { group: "G-1002", status: "Matched", dataset: "Card processor", reference: "CARD/G-1002", amount: 250, date: "14 Oct 2026", variance: "$0" },
  { group: "G-1002", status: "Matched", dataset: "Bank", reference: "DEP-1002", amount: 250, date: "15 Oct 2026", variance: "$0" },
  { group: "G-104482", status: "Open", dataset: "POS", reference: "G-104482", amount: 18420, date: "13 Oct 2026", variance: "No bank row" },
  { group: "DEP-88311", status: "Open", dataset: "Bank", reference: "DEP-88311", amount: 14900, date: "4 Oct 2026", variance: "No POS row" },
  { group: "TX-229184", status: "Open", dataset: "Card processor", reference: "TX-229184", amount: 12640, date: "15 Oct 2026", variance: "Amount differs by $40" },
  { group: "G-104901", status: "Open", dataset: "POS", reference: "G-104901", amount: 9875, date: "10 Sep 2026", variance: "Older than 30 days" },
  { group: "G-2201", status: "Ambiguous", dataset: "POS", reference: "G-2201", amount: 860, date: "16 Oct 2026", variance: "Two bank rows share this key" },
  { group: "G-2201", status: "Ambiguous", dataset: "Bank", reference: "DEP-2201-A", amount: 860, date: "16 Oct 2026", variance: "Duplicate key" },
]

const ongoingGrid: GridRow[] = posGrid.slice(0, 6).map((row) => ({ ...row, status: "Matched" as const }))

const depositGrid: GridRow[] = [
  { group: "DEP-7701", status: "Matched", dataset: "Bank", reference: "DEP-7701", amount: 5400, date: "14 Oct 2026", variance: "$0" },
  { group: "DEP-7701", status: "Matched", dataset: "POS", reference: "BATCH-7701", amount: 5400, date: "14 Oct 2026", variance: "$0" },
  { group: "DEP-7714", status: "Open", dataset: "Bank", reference: "DEP-7714", amount: 2100, date: "14 Oct 2026", variance: "No store batch" },
  { group: "BATCH-7720", status: "Open", dataset: "POS", reference: "BATCH-7720", amount: 960, date: "13 Oct 2026", variance: "No deposit" },
]

const cashupGrid: GridRow[] = [
  { group: "ST-014", status: "Matched", dataset: "POS", reference: "ST-014", amount: 3200, date: "13 Oct 2026", variance: "$0" },
  { group: "ST-014", status: "Matched", dataset: "Bank", reference: "CASH-014", amount: 3185, date: "13 Oct 2026", variance: "$15" },
  { group: "ST-019", status: "Open", dataset: "POS", reference: "ST-019", amount: 740, date: "13 Oct 2026", variance: "Drawer not declared" },
]

const grids: Record<string, GridRow[]> = {
  "run-1042": posGrid,
  "run-1041": ongoingGrid,
  "run-1038": [],
  "run-1033": depositGrid,
  "run-1029": cashupGrid,
}

export function findSampleRun(id: string) {
  return sampleRuns.find((run) => run.id === id)
}

export function gridForRun(id: string) {
  return grids[id] ?? []
}
