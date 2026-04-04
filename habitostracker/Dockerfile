FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app

COPY . .

WORKDIR /app/habitostracker

RUN dotnet restore habitostracker.csproj
RUN dotnet publish habitostracker.csproj -c Release -o out

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app

COPY --from=build /app/habitostracker/out .

ENTRYPOINT ["dotnet", "habitostracker.dll"]