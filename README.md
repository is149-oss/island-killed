# island killed

Windows x64 application built with C#, WPF and .NET 10.

This project was recovered from the compiled Island application and includes subsequent modifications. It is not the original author's source repository. Existing copyright notices are retained. Complete parity with the original application has not been verified.

## Build

Install .NET SDK 10.0.404 and open `Island.slnx`, or run:

```powershell
dotnet restore src/Island.csproj --locked-mode
dotnet publish src/Island.csproj -c DeveloperPro --self-contained true -o artifacts/Island
```

`DeveloperPro` selects local developer access and the `island killed` branding. `999999d` is a display label, not the expiry date of an activation key. Ordinary `Release` keeps the original licensing branch.

Run `artifacts/Island/Island.exe --enable-integrations` to enable the existing local and independent network integrations. The default review mode disables these integrations. Automatic upstream update checks and executable updates are disabled in DeveloperPro.

AI chat requires a configured provider or a compatible custom API. Weather and lyrics use independent services. Android pairing requires a compatible device and setup.

## Social Hub

Add `-p:IslandSocialHub=true` when publishing. Windows notification access requires package identity, the MSIX manifest in `packaging/SocialHub`, and explicit Windows permission. Only notifications actually exposed by Windows can be read; service-specific calls, exact chat navigation and sender avatars are not guaranteed.

## Layout

- `src/`: application code, XAML, fonts and icons.
- `tests/`: local regression and loopback probes with test fixtures.
- `packaging/`: MSIX manifest, assets and packaging script.

The project builds, but not every function has been tested. Static review is not a safety guarantee. No runtime profiles, account credentials, signing private keys or compiled releases are included.
