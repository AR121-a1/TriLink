#include "trilink_usb_network.h"

#include <string.h>
#include "esp_event.h"
#include "esp_log.h"
#include "esp_mac.h"
#include "esp_netif.h"
#include "esp_netif_defaults.h"
#include "esp_netif_net_stack.h"
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"
#include "lwip/inet.h"
#include "lwip/ip4_addr.h"
#include "dhcpserver/dhcpserver.h"
#include "tinyusb_net.h"
#include "trilink_web.h"

#define USB_ETH_FRAME_MAX 1514U
#define USB_ETH_RX_SLOTS 2U
#define USB_ETH_TX_TIMEOUT_MS 20U

static const char *TAG = "trilink_ncm";
static esp_netif_t *s_netif;
static bool s_link_started;
static bool s_bound;
static uint8_t s_mac[6];
static uint8_t s_host_mac[6];
static portMUX_TYPE s_rx_lock = portMUX_INITIALIZER_UNLOCKED;
static struct {
    _Alignas(4) uint8_t bytes[USB_ETH_FRAME_MAX];
    bool used;
} s_rx[USB_ETH_RX_SLOTS];

static void network_free_rx(void *context, void *buffer)
{
    (void)context;
    portENTER_CRITICAL(&s_rx_lock);
    for (size_t i = 0; i < USB_ETH_RX_SLOTS; ++i) {
        if (s_rx[i].bytes == buffer) {
            s_rx[i].used = false;
            break;
        }
    }
    portEXIT_CRITICAL(&s_rx_lock);
}

static esp_err_t network_receive(void *buffer, uint16_t size, void *context)
{
    (void)context;
    if (!s_netif || size < 14U || size > USB_ETH_FRAME_MAX) return ESP_ERR_INVALID_SIZE;
    /* TinyUSB owns buffer until this callback returns; lwIP processes later.
     * Copy to a bounded pool whose free callback releases the exact slot.
     * Exhaustion drops the Ethernet frame and lets TCP/DHCP retry naturally. */
    uint8_t *copy = NULL;
    portENTER_CRITICAL(&s_rx_lock);
    for (size_t i = 0; i < USB_ETH_RX_SLOTS; ++i) {
        if (!s_rx[i].used) {
            s_rx[i].used = true;
            copy = s_rx[i].bytes;
            break;
        }
    }
    portEXIT_CRITICAL(&s_rx_lock);
    if (!copy) return ESP_ERR_NO_MEM;
    memcpy(copy, buffer, size);
    /* ethernetif_input owns/free-callbacks the supplied buffer even on error. */
    return esp_netif_receive(s_netif, copy, size, NULL);
}

static esp_err_t network_transmit(void *context, void *buffer, size_t size)
{
    (void)context;
    if (!buffer || size < 14U || size > USB_ETH_FRAME_MAX || !s_bound) return ESP_ERR_INVALID_SIZE;
    /* esp-netif calls from its single lwIP owner. The sync API copies before
     * returning; do NOT retain the stack's pbuf or use unbounded async calloc. */
    return tinyusb_net_send_sync(buffer, (uint16_t)size, NULL,
                                pdMS_TO_TICKS(USB_ETH_TX_TIMEOUT_MS));
}

esp_err_t trilink_usb_network_prepare(uint8_t device_mac[6])
{
    if (!device_mac || s_netif) return ESP_ERR_INVALID_STATE;
    esp_err_t error = esp_netif_init();
    if (error != ESP_OK) return error;
    error = esp_event_loop_create_default();
    if (error != ESP_OK && error != ESP_ERR_INVALID_STATE) return error;
    error = esp_read_mac(device_mac, ESP_MAC_WIFI_STA);
    if (error != ESP_OK) return error;
    /* Network MAC is locally administered and distinct from ESP-NOW identity. */
    memcpy(s_mac, device_mac, sizeof(s_mac));
    s_mac[0] = (uint8_t)((s_mac[0] | 2U) & 0xFEU);
    memcpy(s_host_mac, s_mac, sizeof(s_host_mac));
    s_host_mac[5] ^= 0x40U;
    s_mac[5] ^= 0x80U;

    esp_netif_ip_info_t address = {0};
    IP4_ADDR(&address.ip, 192, 168, 240, 1);
    IP4_ADDR(&address.netmask, 255, 255, 255, 0);
    /* address.gw stays zero, and router/DNS options are explicitly disabled. */
    esp_netif_inherent_config_t inherent = {
        .flags = ESP_NETIF_DHCP_SERVER | ESP_NETIF_FLAG_AUTOUP,
        .ip_info = &address,
        .if_key = "TRILINK_NCM",
        .if_desc = "TriLink NCM",
        .route_prio = 0,
    };
    memcpy(inherent.mac, s_mac, sizeof(s_mac));
    const esp_netif_driver_ifconfig_t driver = {
        .handle = &s_netif,
        .transmit = network_transmit,
        .driver_free_rx_buffer = network_free_rx,
    };
    const esp_netif_config_t config = {
        .base = &inherent,
        .driver = &driver,
        .stack = ESP_NETIF_NETSTACK_DEFAULT_ETH,
    };
    s_netif = esp_netif_new(&config);
    if (!s_netif) return ESP_ERR_NO_MEM;
    error = esp_netif_set_hostname(s_netif, "trilink-usb");
    uint8_t disabled = 0;
    if (error == ESP_OK) error = esp_netif_dhcps_option(s_netif, ESP_NETIF_OP_SET,
        ESP_NETIF_ROUTER_SOLICITATION_ADDRESS, &disabled, sizeof(disabled));
    if (error == ESP_OK) error = esp_netif_dhcps_option(s_netif, ESP_NETIF_OP_SET,
        ESP_NETIF_DOMAIN_NAME_SERVER, &disabled, sizeof(disabled));
    dhcps_lease_t lease = {.enable = true};
    IP4_ADDR(&lease.start_ip, 192, 168, 240, 2);
    IP4_ADDR(&lease.end_ip, 192, 168, 240, 3);
    if (error == ESP_OK) error = esp_netif_dhcps_option(s_netif, ESP_NETIF_OP_SET,
        ESP_NETIF_REQUESTED_IP_ADDRESS, &lease, sizeof(lease));
    if (error != ESP_OK) {
        esp_netif_destroy(s_netif);
        s_netif = NULL;
    }
    return error;
}

esp_err_t trilink_usb_network_bind(void)
{
    if (!s_netif || s_bound) return ESP_ERR_INVALID_STATE;
    tinyusb_net_config_t config = {
        .on_recv_callback = network_receive,
        .free_tx_buffer = NULL,
        .on_init_callback = NULL,
        .user_context = NULL,
    };
    /* iMACAddress identifies the HOST NCM adapter, not the S3 lwIP endpoint. */
    memcpy(config.mac_addr, s_host_mac, sizeof(s_host_mac));
    esp_err_t error = tinyusb_net_init(&config);
    if (error != ESP_OK) return error;
    s_bound = true;
    error = trilink_web_start();
    if (error != ESP_OK) {
        ESP_LOGE(TAG, "HTTP management unavailable: %s; CDC remains usable", esp_err_to_name(error));
        /* Preserve NCM/CDC operation for diagnostics, never start a second PHY. */
        return error;
    }
    ESP_LOGI(TAG, "local-only 192.168.240.1/24; DHCP .2-.3; no DNS/gateway/NAT");
    return ESP_OK;
}

void trilink_usb_network_poll(bool mounted)
{
    if (!s_netif || !s_bound || mounted == s_link_started) return;
    if (mounted) {
        esp_netif_action_start(s_netif, NULL, 0, NULL);
        esp_netif_action_connected(s_netif, NULL, 0, NULL);
    } else {
        /* IDF 5.5.5 stop_api must see the netif still UP so that it stops
         * DHCP and resets dhcps_status before removing the netif. Calling
         * action_disconnected first would make stop_api return prematurely. */
        esp_netif_action_stop(s_netif, NULL, 0, NULL);
    }
    s_link_started = mounted;
}
