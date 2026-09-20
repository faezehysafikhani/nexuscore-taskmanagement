# NexusCore

NexusCore contains a modular .NET 8 API and a React/Vite administration UI.

## Prerequisites

- .NET SDK 8
- Node.js and npm
- SQL Server with the connection strings configured in
  `NexusCore.Api/appsettings.json` or environment variables

## Run locally

1. Start the full application API (HTTP port 5151):
   `dotnet run --project Rozet.Api/Rozet.Api.csproj --launch-profile http`
2. In a second terminal, install the UI dependencies:
   `npm install`
3. Start the administration UI on port 3030:
   `npm run dev`

The UI development script points `VITE_API_BASE_URL` at
`http://localhost:5151`. Override it when the API runs elsewhere.

## Verify

- UI type-check: `npm run lint`
- UI production build: `npm run build`
- Backend build: `dotnet build NexusCore.sln`
- Backend tests: `dotnet test NexusCore.sln`
