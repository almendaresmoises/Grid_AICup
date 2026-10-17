import { Navigate, Route, Routes } from "react-router"
import { Shell } from "@/components/shell"
import { getToken } from "@/lib/api"
import { AiConfigurePage } from "@/pages/ai-configure-page"
import { ConfigurationPage } from "@/pages/configuration-page"
import { DashboardPage } from "@/pages/dashboard-page"
import { DatasetsPage } from "@/pages/datasets-page"
import { LoginPage } from "@/pages/login-page"
import { ManualConfigurationPage } from "@/pages/manual-configuration-page"
import { PackagesPage } from "@/pages/packages-page"
import { ParserEditorPage } from "@/pages/parser-editor-page"
import { ParsersPage } from "@/pages/parsers-page"
import { ProfilePage } from "@/pages/profile-page"
import { RunGridPage } from "@/pages/run-grid-page"
import { RunPage } from "@/pages/run-page"
import { RunsPage } from "@/pages/runs-page"
import { CreateRunPage } from "@/pages/create-run-page"

function RequireAuth() {
  if (!getToken()) {
    return <Navigate to="/login" replace />
  }
  return <Shell />
}

export function App() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route element={<RequireAuth />}>
        <Route path="/" element={<DashboardPage />} />
        <Route path="/reconciliation/runs" element={<RunsPage />} />
        <Route path="/reconciliation/runs/new" element={<CreateRunPage />} />
        <Route
          path="/reconciliation/runs/:runId/grid"
          element={<RunGridPage />}
        />
        <Route path="/reconciliation/runs/:runId" element={<RunPage />} />
        <Route
          path="/reconciliation/profiles/:profileId"
          element={<ProfilePage />}
        />
        <Route path="/configuration" element={<ConfigurationPage />} />
        <Route path="/configuration/ai" element={<AiConfigurePage />} />
        <Route
          path="/configuration/manual"
          element={<ManualConfigurationPage />}
        >
          <Route index element={<Navigate to="datasets" replace />} />
          <Route path="parsers" element={<ParsersPage />} />
          <Route path="parsers/new" element={<ParserEditorPage />} />
          <Route path="parsers/:parserId" element={<ParserEditorPage />} />
          <Route path="datasets" element={<DatasetsPage />} />
          <Route path="profiles" element={<PackagesPage />} />
        </Route>
        <Route
          path="/ingest"
          element={<Navigate to="/configuration/ai" replace />}
        />
        <Route
          path="/datasets"
          element={<Navigate to="/configuration/manual/datasets" replace />}
        />
        <Route
          path="/reconciliation/packages"
          element={<Navigate to="/configuration/manual/profiles" replace />}
        />
      </Route>
    </Routes>
  )
}
