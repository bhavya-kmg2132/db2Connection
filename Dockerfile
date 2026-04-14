# Multi-stage Linux build for .NET 9 console app
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY *.csproj ./
RUN dotnet restore

COPY . .
RUN dotnet publish -c Release -o ./publish

FROM mcr.microsoft.com/dotnet/runtime:9.0   
WORKDIR /app

COPY --from=build /src/publish .

# Install unixODBC
RUN apt-get update \
  && apt-get install -y --no-install-recommends \
     unixodbc \
     libodbc1 \
  && rm -rf /var/lib/apt/lists/*

# Copy and install IBM i Access Linux ODBC driver (if you have the .deb)
COPY ibmi-driver/ibm-iaccess-1.1.0.29-1.0.amd64.deb /tmp/
RUN apt-get update \
  && apt-get install -y --no-install-recommends /tmp/ibm-iaccess-1.1.0.29-1.0.amd64.deb \
  && rm /tmp/ibm-iaccess-1.1.0.29-1.0.amd64.deb \
  && rm -rf /var/lib/apt/lists/*

# Register ODBC driver
RUN printf '[iSeries Access ODBC Driver]\nDescription=IBM i ODBC\nDriver=/opt/ibm/iaccess/lib64/libcwbodbc.so\nSetup=/opt/ibm/iaccess/lib64/libcwbodbcs.so\nThreading=0\nUsageCount=1\n' >> /etc/odbcinst.ini

# Reload shared libraries
RUN ldconfig

# Expose port(s) for the container (map these at docker run with -p)
EXPOSE 5000
EXPOSE 80

ENV DOTNET_ENVIRONMENT=Production
ENTRYPOINT ["dotnet", "db2Connection.dll"]