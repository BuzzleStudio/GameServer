import { defineConfig } from 'vite'

// Base path for GitHub Pages deployment.
// Override via VITE_BASE env var at build time, e.g.:
//   VITE_BASE=/UnityCloudCode/ npm run build
// Defaults to './' so the build works from any subpath.
const base = process.env.VITE_BASE ?? './'

export default defineConfig({
  base,
  build: {
    outDir: 'dist',
    emptyOutDir: true,
  },
})
