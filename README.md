# Blob Tier Analysis Tool

This tool analyzes the blobs in a storage account and recommends potential cost savings in storage costs
if the blobs are moved from "Hot" or "Cool" tier to "Archive" tier.

## Features

This project framework provides the following features:

* List blobs in a blob container/all blob containers in a storage account.
* Matches blobs against user-defined criteria if they can be moved to "Archive" tier.
* Recommends potential cost savings in monthly storage costs if blobs are moved from "Hot" or "Cool" tier to "Archive" tier.
* Changes access tier from "Hot"/"Cool" to "Archive" tier.

## Getting Started

### Prerequisites


- .Net Framework 4.6
- Azure Storage Client Library version 8.4+.


### Installation

(ideally very short)

- Open solution in Visual Studio 2017.
- Restore Nuget Packages.
- Build solution.
- Add connection string for your Azure Storage account.
- Run the project.
