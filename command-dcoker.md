# Lần đầu (hoặc khi cần reset DB/cache):
docker compose --profile infra up -d

# Hằng ngày — chỉ rebuild source code, không đụng infra:
docker compose up -d --build
