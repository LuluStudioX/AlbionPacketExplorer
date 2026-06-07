FROM nginx:alpine

RUN apk add --no-cache curl jq

COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY sync.sh /usr/local/bin/sync.sh
COPY entrypoint.sh /usr/local/bin/entrypoint.sh
RUN chmod +x /usr/local/bin/sync.sh /usr/local/bin/entrypoint.sh \
    && mkdir -p /srv/feed

ENV FEED_DIR=/srv/feed \
    SYNC_INTERVAL=300

EXPOSE 80
ENTRYPOINT ["/usr/local/bin/entrypoint.sh"]
