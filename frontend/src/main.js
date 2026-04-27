import { createApp } from 'vue'
import './design-tokens.css'
import './base-components.css'
import './style.css'
import App from './App.vue'

const app = createApp(App)

app.config.errorHandler = (err, instance, info) => {
  console.error('[Vue global error]', err, info)
}

app.mount('#app')
