import type { Config } from "tailwindcss";

const config: Config = {
  content: ["./index.html", "./src/**/*.{ts,tsx}"],
  theme: {
    extend: {
      colors: {
        ink: "#132a13",
        sand: "#f7f2e8",
        ember: "#c44536",
        mint: "#2a9d8f",
        sun: "#ffbe0b"
      },
      fontFamily: {
        display: ["Fraunces", "serif"],
        body: ["Manrope", "sans-serif"],
        mono: ["IBM Plex Mono", "monospace"]
      },
      boxShadow: {
        panel: "0 16px 44px -28px rgba(19, 42, 19, 0.55)"
      }
    }
  },
  plugins: []
};

export default config;
