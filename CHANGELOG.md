## 1.1.0 - October 15th 2025
	
### ⚠️ Important Notice
- Added a value truncation setting to config based on Keyfactor and Sectigo field character length limits.
- Setting must be specified for the tool to run - it will exit unless the setting is specified. Please review documentation for the `enableTruncation` setting before using this version!

### Fixed 
- Fixed configuration loading and verification.

### Changed
- Improved logging and improved memory consumption. 
- Updated nuget packages.
- Improved clients for Sectigo and Keyfactor APIs.
- Improved documentation, added diagrams with clarifications.

### Added
- Added OAuth login support for Keyfactor API.
- Added the ability to limit sync to certificates imported into Keyfactor after a given date via config file.
- **Breaking** Added value truncation setting to config.

## 1.0.0 - June 23rd 2025
	
- Initial release
