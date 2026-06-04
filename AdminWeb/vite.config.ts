import { defineConfig } from 'vite'

// Base path for Cloudflare Pages deployment.
// CI passes VITE_BASE=/ so assets are served from the root of *.pages.dev.
// Override via VITE_BASE env var at build time, e.g.:
//   VITE_BASE=/ npm run build
// Defaults to './' so the build works for local dev.
const base = process.env.VITE_BASE ?? './'

export default defineConfig({
  base,
  build: {
    outDir: 'dist',
    emptyOutDir: true,
  },
})
