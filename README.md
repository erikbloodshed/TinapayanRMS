# TinapayanRMS

Recipe management and costing for Tinapayan Festival Bakeshoppe. The app tracks raw material prices. When a price changes, it carries the change up through made ingredients and sub-assemblies to every product's cost and selling price.

## Using the app

Read the **[User Manual](docs/user-manual.md)**. It explains every screen, how to set the app up the first time, and how costs and selling prices are worked out.

## Developing

TinapayanRMS is a Windows desktop app on .NET 11: a .NET MAUI Blazor Hybrid window whose screens are Blazor components with Syncfusion Blazor, over ReactiveUI ViewModels, with PostgreSQL through EF Core. Building it needs the `maui-windows` workload. It was a WPF app until 2026-11-07; the `main` branch still has that one.

Read the **[Developer Manual](docs/developer-manual.md)**. It covers what you need to know and install, how to set up and run the app, the architecture and the patterns the code follows, and how to test and make common changes.

- [CLAUDE.md](CLAUDE.md): commands, and what to know before changing code.
- [PLAN.md](PLAN.md): the project guide. It covers the tech stack, domain model, architecture, costing cascade and conventions.

```sh
dotnet build TinapayanRMS.slnx
dotnet test --solution TinapayanRMS.slnx
dotnet run --project src/TinapayanRMS.Maui
```

Building the app needs the `SYNCFUSION_LICENSE_KEY` user environment variable set to a Syncfusion licence key. The build compiles the key in, so the built app runs without the variable.
