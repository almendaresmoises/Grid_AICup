import type { ReactNode } from "react"

const stats = [
  { label: "Records processed", value: "48,320", detail: "Across 3 sources" },
  { label: "Value matched", value: "$12.4M", detail: "Settled today" },
  { label: "Value still open", value: "$186,420", detail: "283 items" },
  { label: "Match rate", value: "98.4%", detail: "By record count" },
]

const matchedByDay = [
  { day: "3 Oct", value: 11.2 },
  { day: "4 Oct", value: 10.8 },
  { day: "5 Oct", value: 12.1 },
  { day: "6 Oct", value: 13.4 },
  { day: "7 Oct", value: 12.6 },
  { day: "8 Oct", value: 9.4 },
  { day: "9 Oct", value: 11.8 },
  { day: "10 Oct", value: 12.9 },
  { day: "11 Oct", value: 13.1 },
  { day: "12 Oct", value: 11.5 },
  { day: "13 Oct", value: 12.2 },
  { day: "14 Oct", value: 13.6 },
  { day: "15 Oct", value: 12.8 },
  { day: "16 Oct", value: 12.4 },
]

const aging = [
  { label: "Today", amount: 42180 },
  { label: "2–7 days", amount: 68440 },
  { label: "8–30 days", amount: 51080 },
  { label: "Over 30 days", amount: 24720 },
]

const reasons = [
  { label: "Amount difference", count: 86, amount: 74210 },
  { label: "Missing on one side", count: 124, amount: 61840 },
  { label: "Duplicate key", count: 31, amount: 28400 },
  { label: "Date mismatch", count: 42, amount: 21970 },
]

const sources = [
  { label: "POS", amount: 78640 },
  { label: "Card processor", amount: 64210 },
  { label: "Bank", amount: 43570 },
]

const openItems = [
  { source: "POS", reference: "G-104482", age: "3 days", amount: 18420 },
  { source: "Bank", reference: "DEP-88311", age: "12 days", amount: 14900 },
  { source: "Card processor", reference: "TX-229184", age: "1 day", amount: 12640 },
  { source: "POS", reference: "G-104901", age: "36 days", amount: 9875 },
  { source: "Bank", reference: "DEP-88402", age: "6 days", amount: 8210 },
]

export function DashboardPage() {
  return (
    <section className="flex flex-col gap-6">
      <div>
        <h1 className="text-2xl font-medium">Dashboard</h1>
        <p className="text-muted-foreground mt-1 text-sm">Processed through 16 Oct 2026</p>
      </div>

      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        {stats.map((stat) => (
          <article key={stat.label} className="border bg-card px-5 py-4">
            <p className="text-muted-foreground text-sm">{stat.label}</p>
            <p className="mt-2 font-heading text-2xl font-medium tracking-tight">{stat.value}</p>
            <p className="text-muted-foreground mt-1 text-xs">{stat.detail}</p>
          </article>
        ))}
      </div>

      <div className="grid gap-4 lg:grid-cols-5">
        <Panel className="lg:col-span-3" title="Matched value" detail="Last 14 days">
          <MatchedLine points={matchedByDay} />
        </Panel>
        <Panel className="lg:col-span-2" title="Open balance by age" detail="$186,420 still open">
          <AmountBars rows={aging} />
        </Panel>
      </div>

      <div className="grid gap-4 lg:grid-cols-2">
        <Panel title="Why rows are still open" detail="Count and dollars">
          <ReasonList rows={reasons} />
        </Panel>
        <Panel title="Open amount by source" detail="Where the balance sits">
          <AmountBars rows={sources} />
        </Panel>
      </div>

      <Panel title="Largest open items" detail="Highest balances waiting to clear">
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead>
              <tr className="text-muted-foreground border-b text-left text-xs">
                <th className="py-2 pr-4 font-medium">Source</th>
                <th className="py-2 pr-4 font-medium">Reference</th>
                <th className="py-2 pr-4 font-medium">Age</th>
                <th className="py-2 text-right font-medium">Amount</th>
              </tr>
            </thead>
            <tbody>
              {openItems.map((item) => (
                <tr key={item.reference} className="border-b last:border-0">
                  <td className="py-2.5 pr-4">{item.source}</td>
                  <td className="py-2.5 pr-4 font-mono text-xs">{item.reference}</td>
                  <td className="text-muted-foreground py-2.5 pr-4">{item.age}</td>
                  <td className="py-2.5 text-right tabular-nums">{money(item.amount)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </Panel>
    </section>
  )
}

function Panel({ title, detail, className, children }: { title: string; detail: string; className?: string; children: ReactNode }) {
  return (
    <article className={`border bg-card p-5 ${className ?? ""}`}>
      <h2 className="font-medium">{title}</h2>
      <p className="text-muted-foreground mt-1 text-sm">{detail}</p>
      <div className="mt-4">{children}</div>
    </article>
  )
}

function AmountBars({ rows }: { rows: { label: string; amount: number }[] }) {
  const max = Math.max(...rows.map((row) => row.amount))
  return (
    <ul className="flex flex-col gap-3">
      {rows.map((row) => (
        <li key={row.label}>
          <div className="mb-1 flex items-baseline justify-between gap-3 text-sm">
            <span>{row.label}</span>
            <span className="tabular-nums">{money(row.amount)}</span>
          </div>
          <div className="bg-muted h-2">
            <div className="bg-primary h-full" style={{ width: `${(row.amount / max) * 100}%` }} />
          </div>
        </li>
      ))}
    </ul>
  )
}

function ReasonList({ rows }: { rows: { label: string; count: number; amount: number }[] }) {
  const max = Math.max(...rows.map((row) => row.amount))
  return (
    <ul className="flex flex-col gap-3">
      {rows.map((row) => (
        <li key={row.label}>
          <div className="mb-1 flex items-baseline justify-between gap-3 text-sm">
            <span>{row.label}</span>
            <span className="text-muted-foreground tabular-nums">
              {row.count} · {money(row.amount)}
            </span>
          </div>
          <div className="bg-muted h-2">
            <div className="bg-primary h-full" style={{ width: `${(row.amount / max) * 100}%` }} />
          </div>
        </li>
      ))}
    </ul>
  )
}

function MatchedLine({ points }: { points: { day: string; value: number }[] }) {
  const width = 640
  const height = 220
  const pad = { l: 48, r: 8, t: 12, b: 28 }
  const min = 8
  const max = 15
  const x = (index: number) => pad.l + (index / (points.length - 1)) * (width - pad.l - pad.r)
  const y = (value: number) => pad.t + (1 - (value - min) / (max - min)) * (height - pad.t - pad.b)
  const line = points.map((point, index) => `${index === 0 ? "M" : "L"}${x(index).toFixed(1)},${y(point.value).toFixed(1)}`).join(" ")
  const area = `${line} L${x(points.length - 1).toFixed(1)},${height - pad.b} L${x(0).toFixed(1)},${height - pad.b} Z`
  const ticks = [9, 11, 13, 15]
  const labelIndexes = [0, 4, 8, 13]

  return (
    <svg viewBox={`0 0 ${width} ${height}`} className="h-56 w-full" role="img" aria-label="Matched value over the last 14 days">
      {ticks.map((tick) => (
        <g key={tick}>
          <line x1={pad.l} x2={width - pad.r} y1={y(tick)} y2={y(tick)} stroke="var(--border)" />
          <text x={0} y={y(tick) + 4} fill="var(--muted-foreground)" fontSize="11">
            ${tick}M
          </text>
        </g>
      ))}
      <path d={area} fill="var(--primary)" opacity="0.12" />
      <path d={line} fill="none" stroke="var(--primary)" strokeWidth="2" />
      {points.map((point, index) => (
        <circle key={point.day} cx={x(index)} cy={y(point.value)} r="2.5" fill="var(--primary)" />
      ))}
      {labelIndexes.map((index) => (
        <text key={points[index].day} x={x(index)} y={height - 6} textAnchor="middle" fill="var(--muted-foreground)" fontSize="11">
          {points[index].day}
        </text>
      ))}
    </svg>
  )
}

function money(amount: number) {
  return new Intl.NumberFormat("en-US", { style: "currency", currency: "USD", maximumFractionDigits: 0 }).format(amount)
}
