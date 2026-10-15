#pragma once
#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

#define TRILINK_WEB_LINE_MAX 384U
#define TRILINK_WEB_REPLY_MAX 2048U
#define TRILINK_WEB_WAIT_MS 2200U
typedef enum { WEB_IDLE, WEB_QUEUED, WEB_RUNNING, WEB_DONE, WEB_ABANDONED } trilink_web_state_t;
typedef struct {
    trilink_web_state_t state;
    uint32_t ticket, sequence;
    uint64_t deadline;
    size_t reply_length;
    char line[TRILINK_WEB_LINE_MAX];
    char reply[TRILINK_WEB_REPLY_MAX];
} trilink_web_mailbox_t;

/* Pure policy and state machine: callers serialize all access. */
bool trilink_web_allowed_host(const char *host);
bool trilink_web_allowed_origin(const char *origin);
bool trilink_web_local_ipv4(uint32_t local, uint32_t peer);
bool trilink_web_token_equal(const char *expected, const char *provided);
bool trilink_web_deadline(const char *text, uint64_t now, uint64_t *deadline);
bool trilink_web_validate_line(const char *line, size_t length, bool *mutation);
bool trilink_web_enqueue(trilink_web_mailbox_t *box, const char *line,
                          size_t length, uint64_t now, uint32_t *ticket);
bool trilink_web_dequeue(trilink_web_mailbox_t *box, uint64_t now,
                          char *line, size_t capacity, uint32_t *ticket);
void trilink_web_finish(trilink_web_mailbox_t *box, uint32_t ticket,
                        const char *reply, size_t length);
/* 1 result consumed, 0 pending, -1 expired/invalid/abandoned. */
int trilink_web_collect(trilink_web_mailbox_t *box, uint32_t ticket,
                        uint64_t now, char *reply, size_t capacity, size_t *length);
void trilink_web_abandon(trilink_web_mailbox_t *box, uint32_t ticket);
