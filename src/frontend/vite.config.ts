import react from "@vitejs/plugin-react";
import { defineConfig } from "vite";

// Dev only: the API runs behind Traefik on :8080 (compose); the built app is served by the same
// origin (slice #9), so production needs no proxy.
export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      // changeOrigin must stay false: the API's SameOriginMiddleware compares Origin with Host.
      "/api": { target: process.env.PWDMGR_API_URL ?? "http://localhost:8080", changeOrigin: false }
    }
  }
});
