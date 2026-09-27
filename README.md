# TinapayanRMS

Recipe management and costing for Tinapayan Festival Bakeshoppe. The app tracks raw material prices. When a price changes, it carries the change up through made ingredients and sub-assemblies to every product's cost and selling price.

## Using the app

Read the **[User Manual](docs/user-manual.md)**. It explains every screen, how to set the app up the first time, and how costs and selling prices are worked out.

## Developing

TinapayanRMS is a Windows desktop app built with WPF, Syncfusion and ReactiveUI on .NET 10, with SQL Server through EF Core.

- [CLAUDE.md](CLAUDE.md): commands, and what to know before changing code.
- [PLAN.md](PLAN.md): the project guide. It covers the tech stack, domain model, architecture, costing cascade and conventions.

```sh
dotnet build TinapayanRMS.slnx
dotnet test --solution TinapayanRMS.slnx
dotnet run --project src/TinapayanRMS.UI
```

Running the app needs the `SYNCFUSION_LICENSE_KEY` user environment variable set to a Syncfusion licence key.
