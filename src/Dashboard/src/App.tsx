import { Routes, Route } from 'react-router-dom'
import { ProfileProvider, TopBar } from './ui'
import Overview from './pages/Overview'
import Timeline from './pages/Timeline'
import Stats from './pages/Stats'
import Entities from './pages/Entities'
import Tables from './pages/Tables'
import RawExplorer from './pages/RawExplorer'
import Placeholder from './components/Placeholder'

// The redesigned shell: a sticky TopBar nav + a profile slide-over (ProfileProvider)
// available to every page. Page bodies own their own layout/padding.
function App() {
  return (
    <ProfileProvider>
      <TopBar />
      <Routes>
        <Route path="/" element={<Overview />} />
        <Route path="/timeline" element={<Timeline />} />
        <Route path="/stats" element={<Stats />} />
        <Route path="/entities" element={<Entities />} />
        <Route path="/tables" element={<Tables />} />
        <Route path="/raw" element={<RawExplorer />} />
        <Route path="*" element={<Placeholder title="Not found" />} />
      </Routes>
    </ProfileProvider>
  )
}

export default App
