const { env } = require('process');

// During development, "ng serve" forwards API calls to the ASP.NET Core app.
const target = env.ASPNETCORE_HTTPS_PORT ? `https://localhost:${env.ASPNETCORE_HTTPS_PORT}` :
  env.ASPNETCORE_URLS ? env.ASPNETCORE_URLS.split(';')[0] : 'https://localhost:7269';

const PROXY_CONFIG = [
  {
    context: [
      "/api",
      "/health",
      "/hubs",
    ],
    target,
    secure: false,
    // Live updates (SignalR) use a WebSocket, which needs to be forwarded too.
    ws: true
  }
]

module.exports = PROXY_CONFIG;
