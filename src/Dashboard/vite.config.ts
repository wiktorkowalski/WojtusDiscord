import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

// Local dev: `npm run dev` runs Vite on :5173 and proxies /api to the .NET
// backend (DiscordEventService, pinned to :5099 in launchSettings.json).
// Production/Docker: `vite build` emits into the backend's wwwroot, which the
// .NET app serves via UseStaticFiles + MapFallbackToFile. The Docker frontend
// stage overrides outDir to `dist` (the sibling path below does not exist in
// the isolated node stage) and copies dist -> wwwroot.
export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    proxy: {
      '/api': {
        target: 'http://localhost:5099',
        changeOrigin: true,
      },
      // Login gate (#339). No changeOrigin: the OAuth redirect_uri must keep the dev host.
      '/auth': 'http://localhost:5099',
    },
  },
  build: {
    outDir: '../DiscordEventService/wwwroot',
    emptyOutDir: true,
  },
})
