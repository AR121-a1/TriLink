#pragma once

void rgb_worker_fake_log(const char *tag, const char *format, ...);
#define ESP_LOGI(...) rgb_worker_fake_log(__VA_ARGS__)
