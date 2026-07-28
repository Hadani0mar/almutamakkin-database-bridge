# Almutamakkin Database Bridge

Windows bridge for connecting the Almutamakkin mobile application to supported local or network SQL Server sales systems and the barcode print agent.

Each push to `main` is validated, built as a self-contained Windows installer, and published in GitHub Releases by GitHub Actions.

The installer is generated from:

`database_bridge_lab/installer/DatabaseBridgeLab.iss`

Runtime connection profiles and bridge secrets are stored locally and are not part of this repository.
