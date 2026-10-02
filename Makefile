# Which channel this build belongs to. `stable` is what a release publishes and is
# what everything did before the unstable channel existed. `unstable` publishes a
# build of develop into its own manifests, so a tester can add one repository URL
# and nobody pointed at the stable one ever sees those builds.
CHANNEL ?= stable
export CHANNEL

# ?= rather than := so a caller that already computed the version, such as the
# release workflow, stays authoritative. Recomputing per job lets two jobs
# disagree if a commit lands between them.
#
# CHANNEL is handed to the command rather than left to `export`. GNU Make 3.81,
# which is still what macOS ships, does not put an exported variable into the
# environment of a $(shell) evaluated during variable expansion, so `make print
# CHANNEL=unstable` computed a stable version there while computing an unstable one
# on CI's Make 4. Silently building the wrong number on one machine is the whole
# class of bug this file keeps guarding against.
VERSION ?= $(shell CHANNEL=$(CHANNEL) node scripts/next-version.js)
export VERSION
ifeq ($(VERSION),)
$(error Failed to compute VERSION via scripts/next-version.js)
endif

# Which Jellyfin line to build for. See Directory.Build.props.
JELLYFIN_TARGET ?= jf12

# Ask MSBuild what this target compiles to instead of repeating the mapping
# here, where it would drift from Directory.Build.props the first time a target
# moves to a new framework. That drift is exactly what pinned the old zip path
# to net9.0 and would have broken the first multi target release.
TFM = $(shell dotnet msbuild Jellyfin.Plugin.Streamyfin -getProperty:TargetFramework -p:JellyfinTarget=$(JELLYFIN_TARGET) -nologo)
export JELLYFIN_ABI = $(shell dotnet msbuild Jellyfin.Plugin.Streamyfin -getProperty:JellyfinAbi -p:JellyfinTarget=$(JELLYFIN_TARGET) -nologo)

export GITHUB_REPO := streamyfin/jellyfin-plugin-streamyfin
export FILE := streamyfin-$(VERSION)-$(JELLYFIN_TARGET).zip

# The tag the release lives under, which is not always the version. An unstable tag
# must not look like a release tag: next-version.js finds the last release with
# `git describe --match '[0-9]*.[0-9]*'`, so a tag named 0.68.1.56 would become the
# base for the next count, the revision would restart near zero and published
# versions would go backwards. `unstable-` in front keeps it out of that glob.
#
# No slash in it on purpose. A tag with a slash makes the release download path
# ambiguous, and the manifest's sourceUrl is built from this.
export RELEASE_TAG = $(if $(filter unstable,$(CHANNEL)),unstable-$(VERSION),$(VERSION))

# One manifest per channel, and only per channel. Every Jellyfin line shares a file,
# which is what manifest.json has always done: the 62 entries on main carry three
# different targetAbi values between them.
#
# It works because targetAbi is a floor. A 10.11 server drops the jf12 entry and is
# left with the jf11 one; a 12 server keeps both and takes the jf12 one, because the
# writer sorts a shared version by targetAbi descending and the server's sort is
# stable. So a server that moves from 10.11 to 12 is offered the right build without
# anyone editing the URL they configured, which one file per line could never do.
export MANIFEST = $(if $(filter unstable,$(CHANNEL)),manifest-unstable.json,manifest.json)

# How many versions the manifest keeps, counted in versions rather than entries,
# since one version is now two entries. A release manifest keeps every version it
# ever published, because somebody may want to pin an old one. An unstable manifest
# has no such claim on anyone: its entries point at prereleases that exist to be
# replaced, and left unpruned the file grows with every build.
export MANIFEST_KEEP = $(if $(filter unstable,$(CHANNEL)),10,)

print:
	@echo "version=$(VERSION) channel=$(CHANNEL) tag=$(RELEASE_TAG) target=$(JELLYFIN_TARGET) tfm=$(TFM) abi=$(JELLYFIN_ABI) file=$(FILE) manifest=$(MANIFEST)"

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
