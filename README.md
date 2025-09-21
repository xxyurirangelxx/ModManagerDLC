# Mod Manager DLC - Instalador Automático para GTA V

O **Mod Manager DLC** é uma ferramenta de linha de comando para instalar mods de veículos no GTA V. Ele se integra ao **Lenny's Mod Loader (LML)** e automatiza desde o download até a configuração.

## 🚗 Funcionalidades Principais

- **Instalação via URL**: Baixe mods diretamente de links `.zip` ou `.rpf`.
- **Verificação do LML**: Detecta e instala o LML automaticamente se necessário.
- **Suporte a Packs**: Instala veículos individuais ou pacotes `.rpf`.
- **Configuração via `dlc_config.ini`**: Personalize spawn, autor, versão e handling.
- **Instalação de ELS**: Copia arquivos `.xml` para `ELS/pack_default`.
- **Gestão do `mods.xml`**: Ativa o mod automaticamente no LML.

## 🛠️ Como Usar

### 1. Primeiro Uso
- Baixe o `ModManagerDLC.exe`.
- Execute e selecione "2. Selecionar Caminho do GTA V".
- Escolha o `GTA5.exe` na pasta do jogo.
- O programa verifica e instala o LML se necessário.

### 2. Instalando um Mod
- Execute o programa.
- Escolha "1. Instalar Mod pela URL".
- Cole o link direto `.zip` ou `.rpf`.
- Para `.rpf`, pode incluir um `.zip` de metadados com ELS.
- Aguarde a instalação.

## 📦 Criando `dlc_config.ini`

### Para `.zip` de veículo avulso:
```ini
[metadata]
SpawnName=blazer23
Author=SeuNome
Version=1.1
HasEls=true

[handling]
Preset=Chevrolet Trailblazer
```

### Para `.rpf` com metadados:
Estrutura:
```
metadata.zip
├── dlc_config.ini
└── els/
    ├── veiculo1.xml
    └── veiculo2.xml
```

#### Exemplo 1: Veículo único
```ini
[rpf_settings]
DlcName=corolla_cross_pmdf
SpawnName=corcross
HasEls=true
IsVehiclePack=false
```

#### Exemplo 2: Pack de veículos
```ini
[rpf_settings]
DlcName=pack_viaturas_sp
IsVehiclePack=true

[vehicles]
policet = viatura_tatico.xml
police2 = ranger_pmesp.xml
fbi = trailblazer_civil.xml
```
