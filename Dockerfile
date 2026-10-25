# syntax=docker/dockerfile:1

# Etapa 1: compilación y publicación
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["Dsw2025Tpi.Api/Dsw2025Tpi.Api.csproj", "Dsw2025Tpi.Api/"]
COPY ["Dsw2025Tpi.Application/Dsw2025Tpi.Application.csproj", "Dsw2025Tpi.Application/"]
COPY ["Dsw2025Tpi.Data/Dsw2025Tpi.Data.csproj", "Dsw2025Tpi.Data/"]
COPY ["Dsw2025Tpi.Domain/Dsw2025Tpi.Domain.csproj", "Dsw2025Tpi.Domain/"]

RUN dotnet restore "Dsw2025Tpi.Api/Dsw2025Tpi.Api.csproj"

COPY . .

RUN dotnet publish "Dsw2025Tpi.Api/Dsw2025Tpi.Api.csproj" -c Release -o /app/publish --no-restore

# Etapa 2: ejecución
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

EXPOSE 8080

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "Dsw2025Tpi.Api.dll"]