---
services: storage
platforms: dotnet
author: michaelhauss
---

# Blob Tier Analysis Tool

This tool analyzes blobs in a storage account and recommends potential cost savings
when objects are moved between the "Hot", "Cool" and "Archive" tiers.

## Features

This project framework provides the following features:

* Lists blobs in a blob container / all containers in a storage account.
* Matches blobs against user-defined criteria for analysis.
* Recommends potential savings in monthly storage costs when objects are moved between the "Hot", "Cool" and "Archive" tiers.
* Changes access tier of analyzed blobs.

## Getting Started

- Either follow the installation instructions below or find out more about the command line version by running \"BlobTierAnalysisTool.exe /?\"

### Prerequisites

- .Net Framework 4.6
- Azure Storage Client Library version 8.4+.


### Installation

- Open solution in Visual Studio 2017
- Install Nuget Packages
- Build solution
- Run project
