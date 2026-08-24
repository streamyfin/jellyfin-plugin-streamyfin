# ?= rather than := so a caller that already computed the version, such as the
# release workflow, stays authoritative. Recomputing per job lets two jobs
# disagree if a commit lands between them.
VERSION ?= $(shell node scripts/next-version.js)
export VERSION
ifeq ($(VERSION),)
$(error Failed to compute VERSION via scripts/next-version.js)
endif

# Which Jellyfin line to build for. See Directory.Build.props.
JELLYFIN_TARGET ?= jf11

# Ask MSBuild what this target compiles to instead of repeating the mapping
# here, where it would drift from Directory.Build.props the first time a target
# moves to a new framework. That drift is exactly what pinned the old zip path
# to net9.0 and would have broken the first multi target release.
TFM = $(shell dotnet msbuild Jellyfin.Plugin.Streamyfin -getProperty:TargetFramework -p:JellyfinTarget=$(JELLYFIN_TARGET) -nologo)
export JELLYFIN_ABI = $(shell dotnet msbuild Jellyfin.Plugin.Streamyfin -getProperty:JellyfinAbi -p:JellyfinTarget=$(JELLYFIN_TARGET) -nologo)

export GITHUB_REPO := streamyfin/jellyfin-plugin-streamyfin
export FILE := streamyfin-$(VERSION)-$(JELLYFIN_TARGET).zip

# jf11 keeps manifest.json so servers already pointed at that URL keep working.
export MANIFEST = $(if $(filter jf12,$(JELLYFIN_TARGET)),manifest-jf12.json,manifest.json)

print:
	@echo "version=$(VERSION) target=$(JELLYFIN_TARGET) tfm=$(TFM) abi=$(JELLYFIN_ABI) file=$(FILE) manifest=$(MANIFEST)"

build:
	dotnet build Jellyfin.Plugin.Streamyfin --configuration Release -p:JellyfinTarget=$(JELLYFIN_TARGET)

test:
	dotnet test Jellyfin.Plugin.Streamyfin.Tests -p:JellyfinTarget=$(JELLYFIN_TARGET)

zip:
	mkdir -p ./dist
	zip -r -j "./dist/$(FILE)" Jellyfin.Plugin.Streamyfin/bin/Release/$(TFM)/Jellyfin.Plugin.Streamyfin.dll packages/
	cd Jellyfin.Plugin.Streamyfin/bin/Release/$(TFM)/ && find . -type d -not -path '.' -print | zip -ur "$(CURDIR)/dist/$(FILE)" -@

csum:
	md5sum "./dist/$(FILE)"

update-version:
	sed -i 's/\(.*\)<\(.*\)Version>\(.*\)<\/\(.*\)Version>/\1<\2Version>$(VERSION)<\/\4Version>/g' Jellyfin.Plugin.Streamyfin/Jellyfin.Plugin.Streamyfin.csproj

update-manifest:
	node scripts/validate-and-update-manifest.js

.PHONY: print build test zip csum update-version update-manifest
