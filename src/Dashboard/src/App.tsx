import { Routes, Route } from 'react-router-dom'
import AppShell from './components/AppShell'
import Timeline from './pages/Timeline'
import Tables from './pages/Tables'
import TableExplorer from './pages/TableExplorer'
import Placeholder from './components/Placeholder'

// Routes are scaffolded now and filled in over slices S1–S7. AppShell wraps
// all routed content with the persistent sidebar.
function App() {
  return (
    <AppShell>
      <Routes>
        <Route path="/" element={<Timeline />} />
        <Route path="/stats" element={<Placeholder title="Statistics" />} />
        <Route path="/entities" element={<Placeholder title="Entities" />} />
        <Route path="/tables" element={<Tables />} />
        <Route path="/tables/:table" element={<TableExplorer />} />
        <Route path="/raw" element={<Placeholder title="Raw events" />} />
        <Route path="*" element={<Placeholder title="Not found" />} />
      </Routes>
    </AppShell>
  )
}

export default App
