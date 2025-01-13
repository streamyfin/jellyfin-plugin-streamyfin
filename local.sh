if [ ! -f env.local ]; then
    echo "Looks like env.local doesn't exist. Creating... "
    echo "Please set env.local properties."
    
    cat > env.local <<- EOF
CONFIGURATION=Release
REMOTE=false # if true we will send files over via scp
# USER= Set the user name to connect to HOST
# HOST= Host for jellyfin remote server
PLUGIN_PATH=
EOF
    exit 1
fi;

source env.local

if [ -z "$REMOTE" ]; then
  echo "Setting REMOTE in env.local is a requirement."
  exit 1
else
  if [[ "$REMOTE" =~ ^(true|false)$ ]]; then
    if [ -z "$USER" ] || [ -z "$HOST" ]; then
      echo "You must set USER & HOST in env.local if using remote!"
      exit 1
    fi;
  else
    echo "REMOTE in env.local must be a boolean!"
    exit 1
  fi;
fi;

if [ -z "$PLUGIN_PATH" ]; then
  echo "Setting PLUGIN_PATH in env.local is a requirement."
  exit 1
fi;

PLUGIN_VERSION=$(grep '<FileVersion>' < Jellyfin.Plugin.Streamyfin/Jellyfin.Plugin.Streamyfin.csproj | sed 's/.*<FileVersion>\(.*\)<\/FileVersion>/\1/')
echo "Current version: $PLUGIN_VERSION"

dotnet build --configuration $CONFIGURATION

rm -rf ./dist/
mkdir ./dist/

cp ./packages/* ./dist/
cp ./Jellyfin.Plugin.Streamyfin/bin/$CONFIGURATION/net8.0/Jellyfin.Plugin.Streamyfin.dll ./dist/

if [ "$REMOTE" = "true" ]; then
  scp -r ./dist/* $USER@$HOST:$PLUGIN_PATH/Streamyfin_$PLUGIN_VERSION
else
  mkdir -p $PLUGIN_PATH
  cp -R ./dist/ $PLUGIN_PATH/Streamyfin_$PLUGIN_VERSION
fi;
