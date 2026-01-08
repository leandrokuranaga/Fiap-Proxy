# 🕹️ FIAP Proxy

Um API Gateway moderno desenvolvido em .NET 8 utilizando YARP (Yet Another Reverse Proxy) para roteamento de APIs, autenticação, autorização, rate limiting e load balancing.

## 📋 Sobre o Projeto

Esse projeto tem como finalidade servir para roteamento das rotas de apis, além de trabalhar com rate limit, lidar com autenticação, autorização e load balancer, ele fará a comunicação interna e externa com as APIs.

### 🚀 Funcionalidades

- ✅ **Reverse Proxy**: Roteamento inteligente de requisições usando YARP
- ✅ **Health Checks**: Monitoramento de saúde da aplicação
- ✅ **Swagger/OpenAPI**: Documentação automática da API
- ✅ **CORS**: Suporte para Cross-Origin Resource Sharing
- ✅ **Rate Limiting**: Controle de taxa de requisições 
- ✅ **Autenticação/Autorização**: Segurança baseada em JWT 
- ✅ **Load Balancing**: Distribuição de carga entre serviços

### 🛠️ Tecnologias

- **.NET 8**: Framework principal
- **YARP**: Reverse proxy da Microsoft
- **Swagger**: Documentação da API
- **Docker**: Containerização
- **GitHub Actions**: CI/CD
- **AWS ECS**: Deploy em produção

## Build & Tests
| CI | Status |
| --- | --- | 
| Validação PR| [![Pré-validação de Release/Hotfix](https://github.com/leandrokuranaga/fiap-proxy/actions/workflows/validate-release-pr.yml/badge.svg)](https://github.com/leandrokuranaga/fiap-proxy/actions/workflows/validate-release-pr.yml)
| Validação PR Main e Dev | [![Validate PRs for Main and Dev](https://github.com/leandrokuranaga/fiap-proxy/actions/workflows/validate-pr.yml/badge.svg)](https://github.com/leandrokuranaga/fiap-proxy/actions/workflows/validate-pr.yml)
| Release | [![Release on Main (.NET 8)](https://github.com/leandrokuranaga/fiap-proxy/actions/workflows/release.yml/badge.svg)](https://github.com/leandrokuranaga/fiap-proxy/actions/workflows/release.yml)


## Deployment
| CD | Status |
| --- | --- | 
| Dockerhub | [![Publicar Imagem Docker no Docker Hub](https://github.com/leandrokuranaga/fiap-proxy/actions/workflows/deploy-dockerhub.yml/badge.svg)](https://github.com/leandrokuranaga/fiap-proxy/actions/workflows/deploy-dockerhub.yml) | 
| EKS | [![Deploy to AWS EKS](https://github.com/leandrokuranaga/fiap-proxy/actions/workflows/deploy-eks.yml/badge.svg)](https://github.com/leandrokuranaga/fiap-proxy/actions/workflows/deploy-eks.yml) |

## 🚀 Getting Started

### Pré-requisitos

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Docker](https://www.docker.com/get-started) (opcional)
- [Git](https://git-scm.com/)

### 📦 Instalação e Execução

#### 1. Clone o repositório
```bash
git clone https://github.com/leandrokuranaga/fiap-proxy.git
cd fiap-proxy
```

#### 2. Executar com Docker
```bash
# Build da imagem
docker build -t fiap-proxy .

# Executar container
docker run -p 8080:8080 fiap-proxy
```

## 📖 Uso da API

### Endpoints Disponíveis

- **Health Check**: `GET /health` - Verifica o status da aplicação
- **Swagger UI**: `/swagger` - Documentação interativa da API (apenas em desenvolvimento)
- **Proxy Routes**: `/api/*` - Rotas redirecionadas para os serviços configurados

### Configuração do Proxy

A configuração das rotas está em `src/Gateway/appsettings.json`:

```json
{
  "ReverseProxy": {
    "Routes": {
      "default": {
        "ClusterId": "fcgCluster",
        "Match": {
          "Path": "/api/{**catch-all}"
        }
      }
    },
    "Clusters": {
      "fcgCluster": {
        "Destinations": {
          "fcgDestination": {
            "Address": "https://localhost:44323/"
          }
        }
      }
    }
  }
}
```

### Exemplos de Uso

```bash
# Verificar saúde da aplicação
curl http://localhost:8080/health

# Acessar rota proxificada
curl http://localhost:8080/api/seu-endpoint
```

## 🏗️ Arquitetura

O FIAP Proxy segue uma arquitetura de API Gateway moderna baseada em YARP:

```
┌─────────────────┐    ┌─────────────────┐    ┌─────────────────┐
│   Cliente Web   │    │   Mobile App    │    │  Sistema Ext.   │
└─────────┬───────┘    └─────────┬───────┘    └─────────┬───────┘
          │                      │                      │
          └──────────────────────┼──────────────────────┘
                                 │
                    ┌─────────────▼──────────────┐
                    │       FIAP PROXY           │
                    │    (API Gateway)           │
                    │  ┌─────────────────────┐   │
                    │  │   YARP Reverse      │   │
                    │  │      Proxy          │   │
                    │  └─────────────────────┘   │
                    │  ┌─────────────────────┐   │
                    │  │   Health Checks     │   │
                    │  └─────────────────────┘   │
                    │  ┌─────────────────────┐   │
                    │  │   Swagger/OpenAPI   │   │
                    │  └─────────────────────┘   │
                    └─────────────┬──────────────┘
                                  │
          ┌───────────────────────┼───────────────────────┐
          │                       │                       │
┌─────────▼─────────┐   ┌─────────▼─────────┐   ┌─────────▼─────────┐
│   Microserviço    │   │   Microserviço    │   │   Microserviço    │
│       A           │   │       B           │   │       C           │
│  (Auth Service)   │   │ (Business Logic)  │   │  (Data Service)   │
└───────────────────┘   └───────────────────┘   └───────────────────┘
```

### Componentes Principais

- **YARP Reverse Proxy**: Gerencia roteamento e load balancing
- **Health Checks**: Monitora a saúde dos serviços
- **Swagger/OpenAPI**: Documentação automática
- **CORS**: Habilita acesso cross-origin
- **Middleware Pipeline**: Configurável para autenticação, logging, etc.