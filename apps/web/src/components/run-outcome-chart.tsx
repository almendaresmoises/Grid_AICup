import { useId, useState, type PointerEvent } from "react"
import type { DashboardDay } from "@/lib/dashboard"

const series = [
  {
    key: "fullMatchRecords",
    label: "Full match",
    color: "var(--chart-1, #16a34a)",
    dash: undefined,
  },
  {
    key: "partialMatchRecords",
    label: "Partial match",
    color: "var(--chart-2, #d97706)",
    dash: "8 4",
  },
  {
    key: "exceptionRecords",
    label: "Exceptions",
    color: "var(--destructive, #dc2626)",
    dash: "2 5",
  },
] as const
type SeriesKey = (typeof series)[number]["key"]
const width = 1000
const height = 310
const plot = { left: 62, right: 980, top: 24, bottom: 262 }

function label(date: string, long = false) {
  return new Date(`${date}T00:00:00Z`).toLocaleDateString(undefined, {
    month: "short",
    day: "numeric",
    ...(long ? { year: "numeric" } : {}),
    timeZone: "UTC",
  })
}

export function RunOutcomeChart({
  days,
  timeZone,
}: {
  days: DashboardDay[]
  timeZone: string
}) {
  const titleId = useId()
  const [active, setActive] = useState<number | null>(null)
  const [visible, setVisible] = useState<Record<SeriesKey, boolean>>({
    fullMatchRecords: true,
    partialMatchRecords: true,
    exceptionRecords: true,
  })
  const shown = series.filter((item) => visible[item.key])
  const maximum = days.reduce(
    (max, day) =>
      shown.reduce((value, item) => Math.max(value, day[item.key]), max),
    0
  )
  const ceiling = Math.max(4, Math.ceil(maximum / 4) * 4)
  const x = (index: number) =>
    days.length <= 1
      ? (plot.left + plot.right) / 2
      : plot.left + (index / (days.length - 1)) * (plot.right - plot.left)
  const y = (count: number) =>
    plot.bottom - (count / ceiling) * (plot.bottom - plot.top)
  const selected = active === null ? null : days[active]
  const ticks = Array.from(
    new Set([
      0,
      Math.round((days.length - 1) / 4),
      Math.round((days.length - 1) / 2),
      Math.round(((days.length - 1) * 3) / 4),
      days.length - 1,
    ])
  ).filter((index) => index >= 0)

  function point(event: PointerEvent<SVGSVGElement>) {
    const bounds = event.currentTarget.getBoundingClientRect()
    const position = ((event.clientX - bounds.left) / bounds.width) * width
    setActive(
      Math.max(
        0,
        Math.min(
          days.length - 1,
          Math.round(
            ((position - plot.left) / (plot.right - plot.left)) *
              (days.length - 1)
          )
        )
      )
    )
  }

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap gap-2" aria-label="Chart series">
        {series.map((item) => (
          <button
            key={item.key}
            type="button"
            aria-pressed={visible[item.key]}
            className={`flex items-center gap-2 border px-3 py-1.5 text-xs transition-opacity focus-visible:outline-2 focus-visible:outline-ring ${visible[item.key] ? "bg-background" : "opacity-40"}`}
            onClick={() =>
              setVisible((current) => ({
                ...current,
                [item.key]: !current[item.key],
              }))
            }
          >
            <svg width="24" height="8" aria-hidden="true">
              <line
                x1="0"
                x2="24"
                y1="4"
                y2="4"
                stroke={item.color}
                strokeWidth="2.5"
                strokeDasharray={item.dash}
              />
            </svg>
            {item.label}
          </button>
        ))}
      </div>
      <div
        className="relative rounded-sm focus-visible:outline-2 focus-visible:outline-ring"
        tabIndex={0}
        role="group"
        aria-label="Daily record outcomes. Use the left and right arrow keys to inspect dates."
        onFocus={() => setActive(days.length - 1)}
        onKeyDown={(event) => {
          if (event.key === "ArrowLeft" || event.key === "ArrowRight") {
            event.preventDefault()
            setActive((current) =>
              Math.max(
                0,
                Math.min(
                  days.length - 1,
                  (current ?? days.length - 1) +
                    (event.key === "ArrowLeft" ? -1 : 1)
                )
              )
            )
          }
          if (event.key === "Escape") setActive(null)
          if (event.key === "Home") {
            event.preventDefault()
            setActive(0)
          }
          if (event.key === "End") {
            event.preventDefault()
            setActive(days.length - 1)
          }
        }}
      >
        <svg
          viewBox={`0 0 ${width} ${height}`}
          className="block w-full touch-pan-y"
          role="img"
          aria-labelledby={titleId}
          onPointerMove={point}
          onPointerDown={point}
          onPointerLeave={() => setActive(null)}
        >
          <title id={titleId}>
            Daily full matches, partial matches and exception record counts in
            {timeZone}
          </title>
          {[0, 1, 2, 3, 4].map((index) => {
            const count = (ceiling * index) / 4
            return (
              <g key={index}>
                <line
                  x1={plot.left}
                  x2={plot.right}
                  y1={y(count)}
                  y2={y(count)}
                  stroke="var(--border)"
                />
                <text
                  x={plot.left - 12}
                  y={y(count) + 4}
                  textAnchor="end"
                  fontSize="12"
                  fill="var(--muted-foreground)"
                >
                  {new Intl.NumberFormat(undefined, {
                    notation: maximum >= 10000 ? "compact" : "standard",
                  }).format(count)}
                </text>
              </g>
            )
          })}
          {ticks.map((index) => (
            <text
              key={index}
              x={x(index)}
              y={plot.bottom + 28}
              textAnchor="middle"
              fontSize="12"
              fill="var(--muted-foreground)"
            >
              {label(days[index].date)}
            </text>
          ))}
          {shown.map((item) => (
            <path
              key={item.key}
              d={days
                .map(
                  (day, index) =>
                    `${index === 0 ? "M" : "L"}${x(index)},${y(day[item.key])}`
                )
                .join(" ")}
              fill="none"
              stroke={item.color}
              strokeWidth="2.5"
              strokeDasharray={item.dash}
              strokeLinejoin="round"
              vectorEffect="non-scaling-stroke"
            />
          ))}
          {active !== null && selected ? (
            <g>
              <line
                x1={x(active)}
                x2={x(active)}
                y1={plot.top}
                y2={plot.bottom}
                stroke="var(--muted-foreground)"
                strokeDasharray="3 4"
              />
              {shown.map((item) => (
                <circle
                  key={item.key}
                  cx={x(active)}
                  cy={y(selected[item.key])}
                  r="5"
                  stroke={item.color}
                  strokeWidth="2"
                  fill="var(--card)"
                />
              ))}
            </g>
          ) : null}
        </svg>
        {selected ? (
          <div
            className="pointer-events-none absolute top-0 right-1 min-w-48 border bg-popover p-3 text-xs text-popover-foreground shadow-md"
            role="status"
            aria-live="polite"
          >
            <p className="mb-2 font-medium">
              {label(selected.date, true)} · {timeZone}
            </p>
            {series.map((item) => (
              <p key={item.key} className="flex justify-between gap-5 py-0.5">
                <span>{item.label}</span>
                <span className="tabular-nums">
                  {selected[item.key].toLocaleString()}
                </span>
              </p>
            ))}
            <p className="mt-2 border-t pt-2 text-muted-foreground">
              {selected.completedRuns.toLocaleString()} completed runs
            </p>
          </div>
        ) : null}
      </div>
      <p className="text-xs text-muted-foreground">
        Hover or tap to inspect a date. Toggle a legend item to hide or show its
        line. Arrow keys move between dates.
      </p>
    </div>
  )
}
