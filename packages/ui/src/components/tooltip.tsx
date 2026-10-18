import { Tooltip as TooltipPrimitive } from "@base-ui/react/tooltip"
import type { ReactElement, ReactNode } from "react"

export function Tooltip({
  children,
  label,
}: {
  children: ReactElement
  label: ReactNode
}) {
  return (
    <TooltipPrimitive.Provider delay={200}>
      <TooltipPrimitive.Root>
        <TooltipPrimitive.Trigger render={children} />
        <TooltipPrimitive.Portal>
          <TooltipPrimitive.Positioner sideOffset={8} className="z-50">
            <TooltipPrimitive.Popup className="max-w-64 rounded-md border bg-popover px-3 py-2 text-xs font-normal text-popover-foreground shadow-md">
              {label}
            </TooltipPrimitive.Popup>
          </TooltipPrimitive.Positioner>
        </TooltipPrimitive.Portal>
      </TooltipPrimitive.Root>
    </TooltipPrimitive.Provider>
  )
}
