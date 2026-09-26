# YAUI documentation

The documentation site of YAUI, built with [Docusaurus](https://docusaurus.io/) and published to GitHub Pages by `.github/workflows/update-docs.yml` (on each release, or manually).

| Path | Contents |
|---|---|
| `docs/` | The manual (English) |
| `i18n/ja/docusaurus-plugin-content-docs/current/` | The manual (Japanese) |
| `api/` | The API reference, generated (not committed) |
| `api-gen/` | Generates the API reference: docfx reads the runtime sources through `Yaui.csproj`, and [DocFxMarkdownGen](https://github.com/ruccho/DocFxMarkdownGen) (a submodule) turns the result into Markdown |

## Build locally

Requires Node.js 20+, Yarn and the .NET 8 SDK.

```sh
git submodule update --init
cd docs
yarn install
yarn api      # generates api/
yarn start    # or: yarn start --locale ja
```

`yarn build` builds all locales into `build/`.

## Notes

- `Yaui.csproj` does not reference Unity's assemblies, so docfx runs with `allowCompilationErrors` and Unity types are not linked in the API reference.
- The API reference uses the XML documentation comments of **public** members. Comments on private serialized fields do not appear.
