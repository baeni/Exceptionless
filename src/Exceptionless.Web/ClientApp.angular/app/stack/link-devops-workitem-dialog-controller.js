// Abschlussprojekt
(function () {
    "use strict";

    angular.module("app.stack").controller("LinkDevOpsWorkItemDialog", function ($ExceptionlessClient, $uibModalInstance) {
        var vm = this;

        function cancel() {
            $ExceptionlessClient.submitFeatureUsage(vm._source + ".cancel");
            $uibModalInstance.dismiss("cancel");
        }

        function save(isValid) {
            if (!isValid) {
                return;
            }

            $ExceptionlessClient
                .createFeatureUsage(vm._source + ".save")
                .setProperty("devOpsWorkItemId", vm.data.devOpsWorkItemId)
                .submit();
            $uibModalInstance.close(vm.data.devOpsWorkItemId);
        }

        this.$onInit = function $onInit() {
            vm._source = "app.stack.LinkDevOpsWorkItemDialog";
            vm.cancel = cancel;
            vm.save = save;
            $ExceptionlessClient.submitFeatureUsage(vm._source);
        };
    });
})();
