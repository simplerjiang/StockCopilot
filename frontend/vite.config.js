import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

// https://vite.dev/config/
export default defineConfig({
  plugins: [vue()],
  build: {
    // The app shell intentionally keeps the operational workbench in one
    // eager chunk so tab switching stays predictable. Vendor libraries are
    // split below; the remaining app chunk is currently ~675 kB minified.
    chunkSizeWarningLimit: 700,
    rollupOptions: {
      output: {
        manualChunks(id) {
          const normalizedId = id.replaceAll('\\', '/')

          if (normalizedId.includes('/node_modules/')) {
            if (normalizedId.includes('/node_modules/vue/') || normalizedId.includes('/node_modules/@vue/')) {
              return 'vendor-vue'
            }
            if (normalizedId.includes('/node_modules/echarts/') || normalizedId.includes('/node_modules/klinecharts/')) {
              return 'vendor-charts'
            }
            if (normalizedId.includes('/node_modules/marked/') || normalizedId.includes('/node_modules/dompurify/')) {
              return 'vendor-markdown'
            }

            return 'vendor'
          }
        }
      }
    }
  },
  server: {
    proxy: {
      '/api': {
        target: 'http://localhost:5119',
        changeOrigin: true
      }
    }
  }
})
