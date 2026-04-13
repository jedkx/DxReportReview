/** @type {import('tailwindcss').Config} */
module.exports = {
  content: ['./Views/**/*.cshtml'],
  corePlugins: {
    preflight: false
  },
  theme: {
    extend: {
      colors: {
        surface: {
          DEFAULT: '#18181b',
          '1': '#1e1e22',
          '2': '#27272a',
          '3': '#303034'
        },
        line: '#3f3f46'
      }
    }
  }
}
